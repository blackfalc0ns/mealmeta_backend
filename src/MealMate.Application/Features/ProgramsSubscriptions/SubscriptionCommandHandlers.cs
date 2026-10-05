using System.Text.Json;
using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions;

// Validators
public sealed class CreateSubscriptionCommandValidator : AbstractValidator<CreateSubscriptionCommand>
{
    public CreateSubscriptionCommandValidator()
    {
        RuleFor(x => x.CustomerUserId).NotEmpty();
        RuleFor(x => x.ProgramId).NotEmpty();
        RuleFor(x => x.BundleId).NotEmpty();
        RuleFor(x => x.DurationDays).GreaterThan(0);
        RuleFor(x => x.DurationDays).GreaterThanOrEqualTo(6);
        RuleFor(x => x.StartDate).GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow.Date));
    }
}

public sealed class ConfirmSubscriptionPaymentCommandValidator : AbstractValidator<ConfirmSubscriptionPaymentCommand>
{
    public ConfirmSubscriptionPaymentCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.PaymentReference).NotEmpty().MaximumLength(100);
    }
}

// Handlers
public sealed class SubscriptionCommandHandlers :
    ICommandHandler<CreateSubscriptionCommand, SubscriptionDto>,
    ICommandHandler<ConfirmSubscriptionPaymentCommand, SubscriptionDto>,
    IQueryHandler<GetSubscriptionByIdQuery, SubscriptionDto>,
    IQueryHandler<GetSubscriptionsListQuery, PagedResult<SubscriptionDto>>,
    IQueryHandler<GetSubscriptionListStatsQuery, SubscriptionListStatsDto>
{
    private const int ExpiringSoonDays = 7;

    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly ISubscriptionMealSnapshotService? _mealSnapshots;
    private readonly ISubscriptionScheduleService _schedule;
    private readonly ISubscriptionPaymentProcessor? _paymentProcessor;
    private readonly IUserAccountService? _users;
    private readonly ICustomerIndividualSubscriptionGate? _individualGate;

    public SubscriptionCommandHandlers(
        IAppDbContext db,
        IAuditLogger audit,
        IDateTimeProvider clock,
        ISubscriptionMealSnapshotService? mealSnapshots = null,
        ISubscriptionScheduleService? schedule = null,
        ISubscriptionPaymentProcessor? paymentProcessor = null,
        IUserAccountService? users = null,
        ICustomerIndividualSubscriptionGate? individualGate = null)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _mealSnapshots = mealSnapshots;
        _schedule = schedule ?? new SubscriptionScheduleService(clock);
        _paymentProcessor = paymentProcessor;
        _users = users;
        _individualGate = individualGate;
    }

    public async Task<Result<SubscriptionDto>> Handle(CreateSubscriptionCommand request, CancellationToken cancellationToken)
    {
        if (_individualGate is not null)
        {
            var blocking = await _individualGate.FindBlockingAsync(request.CustomerUserId, cancellationToken);
            if (blocking is not null)
            {
                return Error.Conflict(CustomerIndividualSubscriptionGate.ExistingSubscriptionError, "Customer already has a current individual subscription.");
            }
        }

        var program = await _db.Programs.FirstOrDefaultAsync(x => x.Id == request.ProgramId, cancellationToken);
        if (program is null || !program.IsActive || program.Status != ProgramStatus.Active)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Program with ID '{request.ProgramId}' was not found or is inactive.");
        }

        var bundle = await _db.Bundles.FirstOrDefaultAsync(x => x.Id == request.BundleId, cancellationToken);
        if (bundle is null || !bundle.IsActive || bundle.Status != ProgramStatus.Active || bundle.ProgramId != program.Id)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Bundle with ID '{request.BundleId}' was not found or is inactive.");
        }

        var activePriceValues = await _db.RestaurantBundlePricings
            .AsNoTracking()
            .Where(x => x.ProgramId == request.ProgramId
                && x.BundleId == request.BundleId
                && x.EffectiveFromUtc <= _clock.UtcNow
                && (x.EffectiveToUtc == null || x.EffectiveToUtc > _clock.UtcNow)
                && (x.LockStatus == PriceLockStatus.Locked
                    || x.LockStatus == PriceLockStatus.Approved
                    || x.LockStatus == PriceLockStatus.ApprovedWithEffectiveDate))
            .Select(x => x.ReferencePrice26.Amount)
            .ToListAsync(cancellationToken);
        if (activePriceValues.Count == 0)
            return Error.Validation("pricing.unavailable", "No active restaurant pricing data is available for this program and bundle.");
        var pricingRule = await _db.PricingRuleVersions.AsNoTracking().Where(x => x.IsActive).OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (pricingRule is null)
            return Error.Validation("pricing.rules_unavailable", "No active pricing rule version is available.");
        var meanA_t = SubscriptionPackagePricingCalculator.ResolveAverageDailyPrice(
            activePriceValues,
            request.SelectedTier);

        var settingsRow = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, cancellationToken);
        var settings = SubscriptionPricingSettingsReader.Deserialize(settingsRow?.ValueJson);
        var profile = settings.Mode == "per_bundle" && settings.PerBundle.TryGetValue(request.BundleId, out var customProfile)
            ? customProfile
            : settings.Global;
        var rate = profile.ResolveRate(request.DurationDays);
        var serviceDates = _schedule.GetServiceDates(request.StartDate, request.DurationDays, request.IncludeFriday);
        var fridayDays = _schedule.CountFridays(serviceDates);

        // Run pricing calculation using the active global/per-bundle policy. A validated coupon
        // discount lands in the campaign slot, matching the pricing preview the summary showed.
        var pricingReq = new PricingEngineRequest(
            MeanReferenceDailyPrice: meanA_t,
            DurationDays: request.DurationDays,
            CampaignDiscountKwd: request.CouponDiscountKwd,
            DurationRatesPct: new Dictionary<int, decimal> { [request.DurationDays] = rate },
            FridayDays: fridayDays,
            FridayRatePct: profile.FridayRate);

        var pricingResult = PricingEngine.Calculate(pricingReq);

        var endDate = _schedule.GetEndDate(request.StartDate, request.DurationDays, request.IncludeFriday);

        var subscription = new Subscription
        {
            CustomerUserId = request.CustomerUserId,
            ProgramId = request.ProgramId,
            BundleId = request.BundleId,
            SelectedTier = request.SelectedTier,
            DurationDays = request.DurationDays,
            IncludeFriday = request.IncludeFriday,
            Status = SubscriptionStatus.PendingPayment,
            StartDate = request.StartDate,
            EndDate = endDate,
            CountryId = request.CountryId,
            RegionId = request.RegionId,
            DeliveryAddress = request.DeliveryAddress,
            PromoCodeId = request.PromoCodeId,
            CampaignId = request.CampaignId,
            PaidAmount = new Money(pricingResult.FinalCustomerPrice, "KWD"),
            CorrelationId = request.CorrelationId,
            PricingRuleVersionId = pricingRule.Id,
            LastOperationAtUtc = _clock.UtcNow,
        };

        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync(cancellationToken);

        // Fetch commercially-active restaurant reference & supply prices for snapshot
        var activePricings = await _db.RestaurantBundlePricings
            .Where(x => x.ProgramId == request.ProgramId
                && x.BundleId == request.BundleId
                && x.EffectiveFromUtc <= _clock.UtcNow
                && (x.EffectiveToUtc == null || x.EffectiveToUtc > _clock.UtcNow)
                && (x.LockStatus == PriceLockStatus.Locked
                    || x.LockStatus == PriceLockStatus.Approved
                    || x.LockStatus == PriceLockStatus.ApprovedWithEffectiveDate))
            .ToListAsync(cancellationToken);

        var refPricesJson = JsonSerializer.Serialize(activePricings.Select(p => new { p.RestaurantId, Price26 = p.ReferencePrice26.Amount }));
        var supplyPricesJson = JsonSerializer.Serialize(activePricings.Select(p => new { p.RestaurantId, Price26 = p.ContractedSupplyPrice26.Amount }));

        // Fetch region delivery window if RegionId is specified
        string? deliveryWindowStart = null;
        string? deliveryWindowEnd = null;
        if (request.RegionId.HasValue)
        {
            var regionConfig = await _db.RestaurantServiceRegions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RegionId == request.RegionId.Value && x.IsActive, cancellationToken);
            if (regionConfig != null)
            {
                deliveryWindowStart = regionConfig.DeliveryWindowStart.ToString("HH:mm");
                deliveryWindowEnd = regionConfig.DeliveryWindowEnd.ToString("HH:mm");
            }
        }

        var snapshotDetails = JsonSerializer.Serialize(new
        {
            pricingResult.A_t,
            pricingResult.DurationDays,
            pricingResult.DurationMultiplierR,
            pricingResult.UnroundedCorePrice,
            pricingResult.FinalCustomerPrice,
            pricingResult.GatewayCostRecoveryAmount,
            pricingResult.TotalSupplyCost,
            pricingResult.EstimatedPlatformMargin,
            pricingResult.EstimatedPlatformMarginPct,
            pricingResult.IsProfitabilityAlertTriggered,
            CapturedAt = _clock.UtcNow,
            IncludeFriday = request.IncludeFriday,
            FridayDays = fridayDays,
            PricingMode = settings.Mode,
            RegionId = request.RegionId,
            DeliveryWindowStart = deliveryWindowStart,
            DeliveryWindowEnd = deliveryWindowEnd,
            CouponOfferId = request.CouponOfferId,
            CouponDiscountKwd = request.CouponDiscountKwd,
        });

        // Create immutable append-only SubscriptionSnapshot
        var snapshot = new SubscriptionSnapshot
        {
            SubscriptionId = subscription.Id,
            A_t = pricingResult.A_t,
            ROfD = Percentage.FromFraction(pricingResult.DurationMultiplierR),
            NSnapshot = activePricings.Count,
            LimitValue = 1,
            LMin = 1,
            LMax = 3,
            SnapshotJson = snapshotDetails,
            ReferencePricesJson = refPricesJson,
            SupplyPricesJson = supplyPricesJson,
            RuleVersionsJson = JsonSerializer.Serialize(new { PricingRuleVersionId = pricingRule.Id, pricingRule.RuleVersionId, pricingRule.CreatedAtUtc }),
            CapturedAtUtc = _clock.UtcNow,
        };

        _db.SubscriptionSnapshots.Add(snapshot);

        // Record initial SubscriptionOperation
        var operation = new SubscriptionOperation
        {
            SubscriptionId = subscription.Id,
            OperationType = "CREATE",
            PreviousStatus = null,
            NewStatus = SubscriptionStatus.PendingPayment,
            Reason = "Subscription created pending payment",
            PerformedByUserId = request.CustomerUserId,
            PerformedByUserType = UserType.Customer,
            CorrelationId = request.CorrelationId,
            TimestampUtc = _clock.UtcNow,
        };

        _db.SubscriptionOperations.Add(operation);

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "SubscriptionCreated",
            nameof(Subscription),
            subscription.Id,
            After: new { subscription.CustomerUserId, subscription.ProgramId, subscription.BundleId, subscription.PaidAmount.Amount, subscription.Status },
            SourceChannel: "customer-app"), cancellationToken);

        return await GetSubscriptionDtoById(subscription.Id, cancellationToken);
    }

    private static int CountFridaysForServiceDays(DateOnly start, int serviceDays)
    {
        var cursor = start;
        var nonFridayDays = 0;
        var fridays = 0;
        while (nonFridayDays < serviceDays)
        {
            if (cursor.DayOfWeek == DayOfWeek.Friday) fridays++;
            else nonFridayDays++;
            cursor = cursor.AddDays(1);
        }
        return fridays;
    }

    private static DateOnly EndDateForServiceDays(DateOnly start, int serviceDays, bool includeFriday)
    {
        var cursor = start;
        var nonFridayDays = 0;
        while (nonFridayDays < serviceDays)
        {
            if (includeFriday || cursor.DayOfWeek != DayOfWeek.Friday) nonFridayDays++;
            cursor = cursor.AddDays(1);
        }
        return cursor.AddDays(-1);
    }

    public async Task<Result<SubscriptionDto>> Handle(ConfirmSubscriptionPaymentCommand request, CancellationToken cancellationToken)
    {
        if (_paymentProcessor is not null)
        {
            var processed = await _paymentProcessor.VerifyAndProcessAsync(
                request.SubscriptionId,
                request.PaymentReference,
                payloadHash: request.CorrelationId,
                cancellationToken: cancellationToken);
            if (!processed.IsAccepted)
                return Error.Validation(processed.ErrorCode ?? "payment.verification_failed", "MyFatoorah payment verification failed.");
            var verifiedSubscription = await GetSubscriptionDtoById(request.SubscriptionId, cancellationToken);
            return verifiedSubscription is null
                ? Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.")
                : verifiedSubscription;
        }

        var subscription = await _db.Subscriptions
            .Include(x => x.Program)
            .Include(x => x.Bundle)
            .FirstOrDefaultAsync(x => x.Id == request.SubscriptionId, cancellationToken);

        if (subscription is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Subscription with ID '{request.SubscriptionId}' was not found.");
        }

        if (subscription.Status is not (SubscriptionStatus.PendingPayment or SubscriptionStatus.Draft))
        {
            var duplicate = await _db.PaymentWebhookReceipts.AsNoTracking().AnyAsync(x => x.Gateway == "MyFatoorah" && x.EventKey == request.PaymentReference && x.SubscriptionId == subscription.Id, cancellationToken);
            if (duplicate)
            {
                var current = await GetSubscriptionDtoById(subscription.Id, cancellationToken);
                return current is null ? Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.") : current;
            }
            return Error.Validation(MessageCodes.Validation.InvalidStateTransition, $"Cannot confirm payment for subscription in state '{subscription.Status}'.");
        }

        if (await _db.PaymentWebhookReceipts.AsNoTracking().AnyAsync(x => x.Gateway == "MyFatoorah" && x.EventKey == request.PaymentReference, cancellationToken))
        {
            var current = await GetSubscriptionDtoById(subscription.Id, cancellationToken);
            return current is null ? Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.") : current;
        }

        var oldStatus = subscription.Status;
        var holds = await _db.CapacityHolds
            .Where(x => x.SubscriptionId == subscription.Id && x.Status == CapacityHoldStatus.PendingPayment)
            .ToListAsync(cancellationToken);
        if (holds.Any(x => x.ExpiresAtUtc <= _clock.UtcNow))
        {
            foreach (var expired in holds.Where(x => x.ExpiresAtUtc <= _clock.UtcNow))
                expired.Status = CapacityHoldStatus.Expired;
            subscription.Status = SubscriptionStatus.SuspendedForReview;
            subscription.LastOperationAtUtc = _clock.UtcNow;
            _db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt { Gateway = "MyFatoorah", EventKey = request.PaymentReference, SubscriptionId = subscription.Id, Status = "LatePaymentReview", ProcessedAtUtc = _clock.UtcNow });
            _db.SubscriptionOperations.Add(new SubscriptionOperation { SubscriptionId = subscription.Id, OperationType = "LATE_PAYMENT_REVIEW", PreviousStatus = oldStatus, NewStatus = SubscriptionStatus.SuspendedForReview, Reason = "Payment arrived after capacity hold expiry", PerformedByUserId = subscription.CustomerUserId, PerformedByUserType = UserType.Customer, CorrelationId = request.CorrelationId, TimestampUtc = _clock.UtcNow });
            await _db.SaveChangesAsync(cancellationToken);
            return Error.Validation("checkout.capacity_hold_expired", "Payment was received after the restaurant capacity hold expired.");
        }

        var today = DateOnly.FromDateTime(_clock.UtcNow.Date);

        var newStatus = subscription.StartDate > today ? SubscriptionStatus.Scheduled : SubscriptionStatus.Active;

        subscription.Status = newStatus;
        subscription.LastOperationAtUtc = _clock.UtcNow;

        // Holds are the only pre-payment capacity reservation.  Confirmation atomically
        // turns them into permanent subscription days, so later catalog changes cannot
        // alter the fulfilled restaurant/date allocation.
        foreach (var hold in holds)
        {
            hold.Status = CapacityHoldStatus.Confirmed;
            hold.ConfirmedAtUtc = _clock.UtcNow;
            var dayExists = await _db.SubscriptionDays.AnyAsync(x => x.SubscriptionId == subscription.Id && x.ServiceDate == hold.ServiceDate, cancellationToken);
            if (!dayExists)
            {
                _db.SubscriptionDays.Add(new SubscriptionDay
                {
                    SubscriptionId = subscription.Id,
                    RestaurantId = hold.RestaurantId,
                    ResolvedMenuPlanVersionId = hold.MenuPlanVersionId,
                    ServiceDate = hold.ServiceDate,
                    Status = SubscriptionDayStatus.Open,
                    SelectionDeadlineUtc = _schedule.GetSelectionDeadlineUtc(hold.ServiceDate),
                    ConfirmedAtUtc = _clock.UtcNow,
                    IsAutoSelected = false
                });
            }
        }

        if (holds.Count > 0)
        {
            _db.SubscriptionSnapshots.Add(new SubscriptionSnapshot
            {
                SubscriptionId = subscription.Id,
                A_t = subscription.PaidAmount.Amount,
                ROfD = Percentage.FromFraction(1m),
                NSnapshot = holds.Count,
                LimitValue = 1,
                LMin = 1,
                LMax = 1,
                SnapshotJson = JsonSerializer.Serialize(new
                {
                    Type = "checkout_fulfillment_snapshot",
                    RestaurantId = holds[0].RestaurantId,
                    MenuPlanVersionId = holds[0].MenuPlanVersionId,
                    ServiceDates = holds.Select(x => x.ServiceDate),
                    PriceCalculationSnapshot = subscription.PaidAmount.Amount,
                    CapturedAtUtc = _clock.UtcNow
                }),
                ReferencePricesJson = "[]",
                SupplyPricesJson = "[]",
                RuleVersionsJson = "{}",
                CapturedAtUtc = _clock.UtcNow
            });
        }

        var operation = new SubscriptionOperation
        {
            SubscriptionId = subscription.Id,
            OperationType = "PAYMENT_CONFIRMED",
            PreviousStatus = oldStatus,
            NewStatus = newStatus,
            Reason = $"Payment confirmed with ref '{request.PaymentReference}'",
            PerformedByUserId = subscription.CustomerUserId,
            PerformedByUserType = UserType.Customer,
            CorrelationId = request.CorrelationId,
            TimestampUtc = _clock.UtcNow,
        };

        _db.SubscriptionOperations.Add(operation);
        _db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt { Gateway = "MyFatoorah", EventKey = request.PaymentReference, SubscriptionId = subscription.Id, Status = "Confirmed", ProcessedAtUtc = _clock.UtcNow });
        await _db.SaveChangesAsync(cancellationToken);
        if (_mealSnapshots is not null) await _mealSnapshots.CreateMissingAsync(subscription.Id, cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "SubscriptionPaymentConfirmed",
            nameof(Subscription),
            subscription.Id,
            After: new { PreviousStatus = oldStatus, NewStatus = newStatus, request.PaymentReference },
            SourceChannel: "payment-adapter"), cancellationToken);

        return await GetSubscriptionDtoById(subscription.Id, cancellationToken);
    }

    public async Task<Result<SubscriptionDto>> Handle(GetSubscriptionByIdQuery request, CancellationToken cancellationToken)
    {
        var dto = await GetSubscriptionDtoById(request.Id, cancellationToken);
        if (dto is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Subscription with ID '{request.Id}' was not found.");
        }
        return dto;
    }

    public async Task<Result<PagedResult<SubscriptionDto>>> Handle(GetSubscriptionsListQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Subscriptions
            .Include(x => x.Program)
            .Include(x => x.Bundle)
            .AsNoTracking()
            .AsQueryable();

        if (request.CustomerUserId.HasValue) query = query.Where(x => x.CustomerUserId == request.CustomerUserId.Value);
        if (request.Status.HasValue) query = query.Where(x => x.Status == request.Status.Value);
        if (request.ProgramId.HasValue) query = query.Where(x => x.ProgramId == request.ProgramId.Value);
        if (request.BundleId.HasValue) query = query.Where(x => x.BundleId == request.BundleId.Value);
        if (request.IsFamily == true)
        {
            query = query.Where(x => _db.FamilySubscriptions.Any(f => f.PrimarySubscriptionId == x.Id && f.IsActive));
        }
        else if (request.IsFamily == false)
        {
            query = query.Where(x => !_db.FamilySubscriptions.Any(f => f.PrimarySubscriptionId == x.Id && f.IsActive));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageReq = new PageRequest { Page = request.Page, PageSize = request.PageSize };

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(pageReq.Skip)
            .Take(pageReq.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        var dtos = await MapSubscriptionListDtos(items, cancellationToken);

        return new PagedResult<SubscriptionDto>(dtos, totalCount, pageReq.NormalizedPage, pageReq.NormalizedPageSize);
    }

    public async Task<Result<SubscriptionListStatsDto>> Handle(GetSubscriptionListStatsQuery _, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var expiringUntil = today.AddDays(ExpiringSoonDays);
        var familyPrimaryIds = _db.FamilySubscriptions.Where(f => f.IsActive).Select(f => f.PrimarySubscriptionId);
        var individual = _db.Subscriptions.AsNoTracking().Where(s => !familyPrimaryIds.Contains(s.Id));

        var individualActive = await individual.CountAsync(
            s => s.Status == SubscriptionStatus.Scheduled
                || s.Status == SubscriptionStatus.Paid
                || (s.Status == SubscriptionStatus.Active && s.EndDate > expiringUntil),
            cancellationToken);
        var individualExpiring = await individual.CountAsync(
            s => s.Status == SubscriptionStatus.Active && s.EndDate >= today && s.EndDate <= expiringUntil,
            cancellationToken);
        var individualFrozen = await individual.CountAsync(s => s.Status == SubscriptionStatus.Frozen, cancellationToken);
        var individualCancelled = await individual.CountAsync(
            s => s.Status == SubscriptionStatus.Cancelled || s.Status == SubscriptionStatus.CancellationPending,
            cancellationToken);
        var familyGroups = await _db.FamilySubscriptions.AsNoTracking()
            .Where(f => f.IsActive)
            .Select(f => f.PrimarySubscriptionId)
            .Distinct()
            .CountAsync(cancellationToken);
        var familyMembers = await _db.FamilySubscriptions.AsNoTracking()
            .CountAsync(f => f.IsActive, cancellationToken);

        return new SubscriptionListStatsDto(
            individualActive,
            individualFrozen,
            individualExpiring,
            individualCancelled,
            familyGroups,
            familyMembers);
    }

    private async Task<SubscriptionDto?> GetSubscriptionDtoById(Guid id, CancellationToken cancellationToken)
    {
        var sub = await _db.Subscriptions
            .Include(x => x.Program)
            .Include(x => x.Bundle)
            .Include(x => x.Snapshots)
            .Include(x => x.Operations)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return sub is null ? null : MapSubscriptionDto(sub);
    }

    private static SubscriptionDto MapSubscriptionDto(Subscription s) => new(
        s.Id,
        s.CustomerUserId,
        s.ProgramId,
        s.Program?.NameAr ?? "",
        s.Program?.NameEn ?? "",
        s.BundleId,
        s.Bundle?.NameAr ?? "",
        s.Bundle?.NameEn ?? "",
        s.SelectedTier,
        s.DurationDays,
        s.Status,
        s.StartDate,
        s.EndDate,
        s.PaidAmount.Amount,
        s.PaidAmount.CurrencyCode,
        s.DeliveryAddress,
        s.CorrelationId,
        s.CreatedAtUtc,
        s.Snapshots?.Select(snp => new SubscriptionSnapshotDto(
            snp.Id,
            snp.SubscriptionId,
            snp.A_t,
            snp.ROfD.Value,
            snp.SnapshotJson,
            snp.CapturedAtUtc)).ToList() ?? (IReadOnlyList<SubscriptionSnapshotDto>)Array.Empty<SubscriptionSnapshotDto>(),
        s.Operations?.Select(op => new SubscriptionOperationDto(
            op.Id,
            op.SubscriptionId,
            op.OperationType,
            op.PreviousStatus,
            op.NewStatus,
            op.Reason,
            op.PerformedByUserId,
            op.PerformedByUserType,
            op.CorrelationId,
            op.TimestampUtc)).ToList() ?? (IReadOnlyList<SubscriptionOperationDto>)Array.Empty<SubscriptionOperationDto>(),
        s.IncludeFriday);

    private async Task<List<SubscriptionDto>> MapSubscriptionListDtos(
        List<Subscription> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var ids = items.Select(x => x.Id).ToList();
        var customerIds = items.Select(x => x.CustomerUserId).Distinct().ToList();
        var regionIds = items.Where(x => x.RegionId.HasValue).Select(x => x.RegionId!.Value).Distinct().ToList();

        var identityById = _users is null
            ? new Dictionary<Guid, UserIdentityDto>()
            : (await _users.GetUserIdentitiesAsync(customerIds, cancellationToken))
                .GroupBy(x => x.Id)
                .ToDictionary(g => g.Key, g => g.First());

        var regionById = new Dictionary<Guid, (string NameAr, string NameEn)>();
        if (regionIds.Count > 0)
        {
            var regions = await _db.Regions.AsNoTracking()
                .Where(r => regionIds.Contains(r.Id))
                .Select(r => new { r.Id, r.NameAr, r.NameEn })
                .ToListAsync(cancellationToken);
            regionById = regions.ToDictionary(r => r.Id, r => (r.NameAr, r.NameEn));
        }

        var familyCounts = await _db.FamilySubscriptions.AsNoTracking()
            .Where(f => ids.Contains(f.PrimarySubscriptionId) && f.IsActive)
            .GroupBy(f => f.PrimarySubscriptionId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var familyCountById = familyCounts.ToDictionary(x => x.Id, x => x.Count);

        var deliveredCounts = await _db.SubscriptionDays.AsNoTracking()
            .Where(d => ids.Contains(d.SubscriptionId) && d.Status == SubscriptionDayStatus.Delivered)
            .GroupBy(d => d.SubscriptionId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var deliveredById = deliveredCounts.ToDictionary(x => x.Id, x => x.Count);

        var today = DateOnly.FromDateTime(_clock.UtcNow);

        return items.Select(s =>
        {
            identityById.TryGetValue(s.CustomerUserId, out var user);
            string? regionAr = null;
            string? regionEn = null;
            if (s.RegionId is Guid regionId && regionById.TryGetValue(regionId, out var region))
            {
                regionAr = region.NameAr;
                regionEn = region.NameEn;
            }

            var serviceDates = _schedule.GetServiceDates(s.StartDate, s.DurationDays, s.IncludeFriday);
            var totalServiceDays = serviceDates.Count > 0 ? serviceDates.Count : s.DurationDays;
            var usedDays = deliveredById.TryGetValue(s.Id, out var delivered)
                ? delivered
                : serviceDates.Count(d => d < today);
            var remainingDays = Math.Max(0, totalServiceDays - usedDays);
            familyCountById.TryGetValue(s.Id, out var memberCount);

            var dto = MapSubscriptionDto(s);
            return dto with
            {
                CustomerFullName = user?.FullName,
                CustomerEmail = user?.Email,
                CustomerPhone = user?.PhoneNumber,
                RegionId = s.RegionId,
                RegionNameAr = regionAr,
                RegionNameEn = regionEn,
                UsedDays = usedDays,
                RemainingDays = remainingDays,
                TotalServiceDays = totalServiceDays,
                IsFamily = memberCount > 0,
                FamilyMemberCount = memberCount,
            };
        }).ToList();
    }
}
