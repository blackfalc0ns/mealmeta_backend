using System.Text.Json;
using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
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
    IQueryHandler<GetSubscriptionsListQuery, PagedResult<SubscriptionDto>>
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public SubscriptionCommandHandlers(IAppDbContext db, IAuditLogger audit, IDateTimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<SubscriptionDto>> Handle(CreateSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var program = await _db.Programs.FirstOrDefaultAsync(x => x.Id == request.ProgramId, cancellationToken);
        if (program is null || !program.IsActive)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Program with ID '{request.ProgramId}' was not found or is inactive.");
        }

        var bundle = await _db.Bundles.FirstOrDefaultAsync(x => x.Id == request.BundleId, cancellationToken);
        if (bundle is null || !bundle.IsActive)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, $"Bundle with ID '{request.BundleId}' was not found or is inactive.");
        }

        // Get latest classification run for Program & Bundle to get A_t
        var run = await _db.ClassificationRuns
            .Include(x => x.Classifications)
            .Where(x => x.ProgramId == request.ProgramId && x.BundleId == request.BundleId)
            .OrderByDescending(x => x.RunAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        decimal meanA_t = 3.500m;
        if (run is not null)
        {
            var tierItems = run.Classifications.Where(x => x.Tier == request.SelectedTier).ToList();
            meanA_t = tierItems.Any() ? tierItems.Average(x => x.ReferenceDailyBox) : run.MeanReferenceDaily;
        }

        // Run pricing calculation
        var pricingReq = new PricingEngineRequest(
            MeanReferenceDailyPrice: meanA_t,
            DurationDays: request.DurationDays);

        var pricingResult = PricingEngine.Calculate(pricingReq);

        var endDate = request.StartDate.AddDays(request.DurationDays);

        var subscription = new Subscription
        {
            CustomerUserId = request.CustomerUserId,
            ProgramId = request.ProgramId,
            BundleId = request.BundleId,
            SelectedTier = request.SelectedTier,
            DurationDays = request.DurationDays,
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
            LastOperationAtUtc = _clock.UtcNow,
        };

        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync(cancellationToken);

        // Fetch active restaurant reference & supply prices for snapshot
        var activePricings = await _db.RestaurantBundlePricings
            .Where(x => x.ProgramId == request.ProgramId && x.BundleId == request.BundleId && x.EffectiveToUtc == null)
            .ToListAsync(cancellationToken);

        var refPricesJson = JsonSerializer.Serialize(activePricings.Select(p => new { p.RestaurantId, Price26 = p.ReferencePrice26.Amount }));
        var supplyPricesJson = JsonSerializer.Serialize(activePricings.Select(p => new { p.RestaurantId, Price26 = p.ContractedSupplyPrice26.Amount }));

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
            RuleVersionsJson = "{}",
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

    public async Task<Result<SubscriptionDto>> Handle(ConfirmSubscriptionPaymentCommand request, CancellationToken cancellationToken)
    {
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
            return Error.Validation(MessageCodes.Validation.InvalidStateTransition, $"Cannot confirm payment for subscription in state '{subscription.Status}'.");
        }

        var oldStatus = subscription.Status;
        var today = DateOnly.FromDateTime(_clock.UtcNow.Date);

        var newStatus = subscription.StartDate > today ? SubscriptionStatus.Scheduled : SubscriptionStatus.Active;

        subscription.Status = newStatus;
        subscription.LastOperationAtUtc = _clock.UtcNow;

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
        await _db.SaveChangesAsync(cancellationToken);

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
            .Include(x => x.Snapshots)
            .Include(x => x.Operations)
            .AsNoTracking()
            .AsQueryable();

        if (request.CustomerUserId.HasValue) query = query.Where(x => x.CustomerUserId == request.CustomerUserId.Value);
        if (request.Status.HasValue) query = query.Where(x => x.Status == request.Status.Value);
        if (request.ProgramId.HasValue) query = query.Where(x => x.ProgramId == request.ProgramId.Value);
        if (request.BundleId.HasValue) query = query.Where(x => x.BundleId == request.BundleId.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var pageReq = new PageRequest { Page = request.Page, PageSize = request.PageSize };

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(pageReq.Skip)
            .Take(pageReq.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(MapSubscriptionDto).ToList();

        return new PagedResult<SubscriptionDto>(dtos, totalCount, pageReq.NormalizedPage, pageReq.NormalizedPageSize);
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
            op.TimestampUtc)).ToList() ?? (IReadOnlyList<SubscriptionOperationDto>)Array.Empty<SubscriptionOperationDto>());
}
