using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MediatR;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MealMate.Application.Features.FamilyCheckout;

public sealed record FamilyStartCheckoutRequest(
    string CallbackUrl,
    string ErrorUrl,
    string? CorrelationId = null,
    int PaymentMethodId = 3,
    string? IdempotencyKey = null,
    Guid? BatchId = null,
    long? BatchVersion = null,
    int? ClientContractVersion = null,
    string? PromoCode = null);

public sealed record FamilyRenewalCheckoutRequest(
    DateOnly StartDate,
    Guid AddressId,
    string CallbackUrl,
    string ErrorUrl,
    string? CorrelationId = null,
    int PaymentMethodId = 3,
    string? IdempotencyKey = null,
    int? ClientContractVersion = null);

public sealed record FamilyCheckoutStartedDto(
    Guid FamilyGroupId,
    Guid PaymentAnchorSubscriptionId,
    string PaymentUrl,
    DateTime HoldExpiresAtUtc,
    decimal FinalPrice,
    Guid? BatchId = null,
    long? BatchVersion = null);

public interface IFamilyCheckoutService
{
    Task<Result<FamilyCheckoutStartedDto>> StartAsync(FamilyStartCheckoutRequest request, CancellationToken cancellationToken = default);
    Task<Result<FamilyCheckoutStartedDto>> StartRenewalAsync(Guid renewalRequestId, FamilyRenewalCheckoutRequest request, CancellationToken cancellationToken = default);
}

public sealed class FamilyCheckoutService : IFamilyCheckoutService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IUserAccountService _users;
    private readonly IFamilyQuoteService _quote;
    private readonly ISubscriptionScheduleService _schedule;
    private readonly IRestaurantEligibilityService _eligibility;
    private readonly ICapacityConcurrencyLock _capacityLock;
    private readonly ICheckoutTransactionExecutor _transactions;
    private readonly IRestaurantPoolSnapshotService _restaurantPool;
    private readonly IPaymentGatewayService _payments;
    private readonly ISender _sender;
    private readonly IDateTimeProvider _clock;
    private readonly IRestaurantCapacityReader _capacityReader;
    private readonly IApplicationLockManager _locks;
    private readonly IFamilySubscriptionAccess _familyAccess;
    private readonly IFamilyPricingSettingsReader _familyPricing;
    private readonly ISubscriptionFlowCouponService _coupons;
    private readonly FamilyInvoiceOptions _invoiceOptions;

    public FamilyCheckoutService(
        IAppDbContext db,
        ICurrentUser currentUser,
        IUserAccountService users,
        IFamilyQuoteService quote,
        ISubscriptionScheduleService schedule,
        IRestaurantEligibilityService eligibility,
        ICapacityConcurrencyLock capacityLock,
        ICheckoutTransactionExecutor transactions,
        IRestaurantPoolSnapshotService restaurantPool,
        IPaymentGatewayService payments,
        ISender sender,
        IDateTimeProvider clock,
        IRestaurantCapacityReader capacityReader,
        IApplicationLockManager locks,
        IFamilySubscriptionAccess? familyAccess = null,
        IFamilyPricingSettingsReader? familyPricing = null,
        IOptions<FamilyInvoiceOptions>? invoiceOptions = null,
        ISubscriptionFlowCouponService? coupons = null)
    {
        _db = db;
        _currentUser = currentUser;
        _users = users;
        _quote = quote;
        _schedule = schedule;
        _eligibility = eligibility;
        _capacityLock = capacityLock;
        _transactions = transactions;
        _restaurantPool = restaurantPool;
        _payments = payments;
        _sender = sender;
        _clock = clock;
        _capacityReader = capacityReader;
        _locks = locks;
        _familyAccess = familyAccess ?? new FamilySubscriptionAccessService(db, new FamilyActionPolicy(db));
        _familyPricing = familyPricing ?? new FamilyPricingSettingsReader(db);
        _coupons = coupons ?? new SubscriptionFlowCouponService(db, new MealMate.Application.Features.Offers.Services.OfferEligibilityEngine(), clock, locks);
        _invoiceOptions = invoiceOptions?.Value ?? new FamilyInvoiceOptions();
    }

    public async Task<Result<FamilyCheckoutStartedDto>> StartRenewalAsync(
        Guid renewalRequestId, FamilyRenewalCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserType != UserType.Customer || _currentUser.UserId is not Guid actorId)
            return Error.Forbidden("auth.forbidden", "auth.forbidden");
        if (renewalRequestId == Guid.Empty || request.AddressId == Guid.Empty || request.StartDate == default)
            return Error.Validation("family.renewal_configuration_invalid");

        var route = await _db.SubscriptionRenewals.AsNoTracking()
            .Where(x => x.Id == renewalRequestId)
            .Select(x => new { x.FamilyGroupId, x.PreviousSubscriptionId })
            .FirstOrDefaultAsync(cancellationToken);
        if (route?.FamilyGroupId is not Guid groupId)
            return Error.NotFound("subscription.renewal_not_found");
        var paymentMethodId = request.PaymentMethodId <= 0 ? 3 : request.PaymentMethodId;
        var fingerprintPayload = string.Join("\n", actorId.ToString("D", CultureInfo.InvariantCulture),
            request.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), request.AddressId.ToString("D", CultureInfo.InvariantCulture),
            request.CallbackUrl, request.ErrorUrl, paymentMethodId.ToString(CultureInfo.InvariantCulture),
            request.CorrelationId ?? string.Empty, request.IdempotencyKey?.Trim() ?? string.Empty);
        var checkoutFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintPayload)));

        var reservationResult = await _transactions.ExecuteResultAsync<FamilyReservationOutcome>(async ct =>
        {
            await _locks.LockFamilyGroupAsync(groupId, ct);
            await _locks.LockSubscriptionAsync(route.PreviousSubscriptionId, ct);
            await _locks.LockSubscriptionDaysAsync(route.PreviousSubscriptionId, ct);

            var renewal = await _db.SubscriptionRenewals.FirstOrDefaultAsync(x => x.Id == renewalRequestId, ct);
            var previous = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == route.PreviousSubscriptionId, ct);
            var group = await _db.FamilyGroups.Include(x => x.Members).ThenInclude(x => x.MemberSubscription)
                .FirstOrDefaultAsync(x => x.Id == groupId, ct);
            if (renewal is null || previous is null || group is null || previous.FamilyGroupId != groupId
                || renewal.FamilyGroupId != groupId || renewal.PreviousSubscriptionId != previous.Id)
                return Error.Conflict("family.renewal_intent_changed");

            var authorization = await _familyAccess.AuthorizeAsync(previous, actorId, FamilyAction.RequestRenewal, ct);
            if (authorization.IsFailure) return authorization.Error;
            if (authorization.Value.MemberId != previous.FamilyMemberId || previous.FamilyMemberId is not Guid memberId
                || group.Members.All(x => x.Id != memberId || x.IsDeleted))
                return Error.Conflict("family.renewal_member_changed");

            if (renewal.BatchId is Guid existingBatchId)
            {
                var existingBatch = await _db.FamilyCheckoutBatches.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == existingBatchId && x.RenewalRequestId == renewalRequestId, ct);
                var existingAttempt = existingBatch is null ? null : await _db.FamilyCheckoutPaymentAttempts.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.BatchId == existingBatchId, ct);
                var existingLine = await _db.FamilyCheckoutBatchLines.AsNoTracking().SingleOrDefaultAsync(x => x.BatchId == existingBatchId
                    && x.CheckoutSelected && x.RenewalRequestId == renewalRequestId, ct);
                var existingNext = existingLine?.SubscriptionId is Guid existingNextId
                    ? await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == existingNextId, ct) : null;
                var savedServiceDates = existingLine?.StartDate is DateOnly savedStartDate
                    ? _schedule.GetServiceDates(savedStartDate, previous.DurationDays, previous.IncludeFriday) : [];
                var existingNextIdValue = existingNext?.Id;
                var cachedServiceOverlaps = savedServiceDates.Count == 0 || await _db.Subscriptions.AsNoTracking().AnyAsync(x =>
                    x.FamilyGroupId == groupId && x.FamilyMemberId == memberId && x.Id != previous.Id
                    && (!existingNextIdValue.HasValue || x.Id != existingNextIdValue.Value)
                    && (x.Status == SubscriptionStatus.Paid || x.Status == SubscriptionStatus.Active
                        || x.Status == SubscriptionStatus.Scheduled || x.Status == SubscriptionStatus.Frozen
                        || x.Status == SubscriptionStatus.SuspendedForReview || x.Status == SubscriptionStatus.CancellationPending
                        || x.Status == SubscriptionStatus.PendingPayment)
                    && x.StartDate <= savedServiceDates[savedServiceDates.Count - 1] && x.EndDate >= savedServiceDates[0], ct);
                if (!string.Equals(renewal.CheckoutFingerprint, checkoutFingerprint, StringComparison.Ordinal)
                    || existingBatch?.PayerCustomerUserId != actorId)
                    return Error.Conflict("family.renewal_idempotency_mismatch");
                if (existingBatch?.Purpose == "Renewal"
                    && existingBatch.Status == FamilyCheckoutBatchStatus.Paid
                    && existingAttempt?.ResultCode == "Paid"
                    && existingAttempt.GatewayPaymentUrl is { Length: > 0 } paidUrl
                    && existingLine?.PreviousSubscriptionId == previous.Id
                    && existingLine.RenewalRequestId == renewalRequestId
                    && existingNext is not null
                    && existingNext.Status is SubscriptionStatus.Paid or SubscriptionStatus.Active or SubscriptionStatus.Scheduled
                        or SubscriptionStatus.Frozen or SubscriptionStatus.Completed or SubscriptionStatus.Expired
                    && renewal.Status == "Completed"
                    && renewal.NewSubscriptionId == existingNext.Id
                    && existingNext.FamilyGroupId == groupId && existingNext.FamilyMemberId == memberId
                    && group.Members.FirstOrDefault(x => x.Id == memberId)?.MemberSubscriptionId == existingNext.Id
                    && existingBatch.PaymentAnchorSubscriptionId == existingNext.Id
                    && existingBatch.PayerCustomerUserId == actorId
                    && existingLine.StartDate == request.StartDate && renewal.RequestedStartDate == request.StartDate
                    && renewal.RequestedAddressId == request.AddressId)
                    return new FamilyReservationOutcome(groupId, existingNext.Id, [existingNext.Id],
                        existingBatch.PaidAtUtc ?? existingAttempt.VerifiedAtUtc ?? existingAttempt.RequestedAtUtc,
                        existingBatch.FinalPrice, [], existingBatch.Id, existingBatch.Version, existingAttempt.Id,
                        false, paidUrl);
                if (existingBatch?.Status == FamilyCheckoutBatchStatus.PaymentPending
                    && existingBatch.Purpose == "Renewal"
                    && existingAttempt?.GatewayPaymentUrl is { Length: > 0 } savedUrl
                    && existingAttempt.ResultCode == "Initialized"
                    && existingBatch.PaymentAnchorSubscriptionId is Guid savedAnchor
                    && existingBatch.ExpiresAtUtc > _clock.UtcNow
                    && renewal.Status == "PaymentPending"
                    && group.Members.FirstOrDefault(x => x.Id == memberId)?.MemberSubscriptionId == previous.Id
                    && previous.Status is SubscriptionStatus.Paid or SubscriptionStatus.Active or SubscriptionStatus.Scheduled
                        or SubscriptionStatus.Frozen or SubscriptionStatus.SuspendedForReview
                        or SubscriptionStatus.Completed or SubscriptionStatus.Expired
                    && existingLine?.PreviousSubscriptionId == previous.Id
                    && existingNext is not null && existingNext.Status == SubscriptionStatus.PendingPayment
                    && existingNext.StartDate == request.StartDate && renewal.RequestedStartDate == request.StartDate
                    && renewal.RequestedAddressId == request.AddressId
                    && existingLine.DeliveryAddressId == request.AddressId
                    && existingLine.StartDate == request.StartDate
                    && savedServiceDates.Count > 0 && savedServiceDates[0] > previous.EndDate && !cachedServiceOverlaps)
                    return new FamilyReservationOutcome(groupId, savedAnchor, [savedAnchor],
                        existingBatch.ExpiresAtUtc ?? existingAttempt.RequestedAtUtc, existingBatch.FinalPrice, [],
                        existingBatch.Id, existingBatch.Version, existingAttempt.Id, false, savedUrl);
                if (existingBatch?.Status == FamilyCheckoutBatchStatus.PaymentPending
                    && (existingAttempt?.ResultCode == "Initialized" || existingBatch.ExpiresAtUtc <= _clock.UtcNow
                        || existingAttempt?.GatewayPaymentUrl is { Length: > 0 }))
                {
                    var trackedBatch = await _db.FamilyCheckoutBatches.FirstAsync(x => x.Id == existingBatch.Id, ct);
                    trackedBatch.Status = FamilyCheckoutBatchStatus.ReviewRequired;
                    trackedBatch.Version++;
                    renewal.Status = "ReviewRequired";
                    await _db.SaveChangesAsync(ct);
                    return new FamilyReservationOutcome(groupId, previous.Id, [previous.Id], _clock.UtcNow,
                        0m, [], trackedBatch.Id, trackedBatch.Version, existingAttempt?.Id ?? Guid.Empty,
                        false, FailureCode: "family.batch_review_required");
                }
                return Error.Conflict(existingBatch?.Status == FamilyCheckoutBatchStatus.ReviewRequired
                    ? "family.batch_review_required" : "family.payment_initializing");
            }
            if (renewal.Status is not ("Requested" or "ReadyForCheckout"))
                return Error.Conflict("family.renewal_not_available");
            if (await _db.FamilyCheckoutBatches.AsNoTracking().AnyAsync(x => x.FamilyGroupId == groupId
                && (x.Status == FamilyCheckoutBatchStatus.PaymentPending || x.Status == FamilyCheckoutBatchStatus.ReviewRequired), ct))
                return Error.Conflict("family.payment_in_flight");
            if (previous.Status is not (SubscriptionStatus.Paid or SubscriptionStatus.Active or SubscriptionStatus.Scheduled
                    or SubscriptionStatus.Frozen or SubscriptionStatus.Completed or SubscriptionStatus.Expired))
                return Error.Conflict("family.renewal_not_available");
            var previousPaid = previous.PaymentVerifiedAtUtc.HasValue
                || await _db.FamilyCheckoutBatchLines.AsNoTracking().AnyAsync(x => x.SubscriptionId == previous.Id
                    && x.CheckoutSelected && _db.FamilyCheckoutBatches.Any(b => b.Id == x.BatchId
                        && b.Status == FamilyCheckoutBatchStatus.Paid), ct);
            if (!previousPaid) return Error.Conflict("family.renewal_not_available");

            var member = group.Members.Single(x => x.Id == memberId);
            var participationReader = new FamilyParticipationContextReader(_db, _clock, _familyPricing);
            var participation = await participationReader.GetAsync(group, new HashSet<Guid> { memberId }, ct);
            var consumedManagerExemption = false;
            if (member.Role == FamilyMemberRole.Manager)
            {
                if (participation.ManagerIndividualIsLive
                    && participation.ManagerLiveIndividualSubscriptionId != previous.Id)
                    return Error.Conflict("family.already_subscribed", "The manager has an overlapping individual service.");

                if (participation.ManagerSeatExempt)
                {
                    group.ManagerIndividualSeatExempt = false;
                    consumedManagerExemption = true;
                    participation = await participationReader.GetAsync(group, new HashSet<Guid> { memberId }, ct);
                }
            }

            if (participation.MaxSeats > 0 && participation.OccupiedSeats > participation.MaxSeats)
            {
                if (consumedManagerExemption) group.ManagerIndividualSeatExempt = true;
                return Error.Conflict("family.max_members", "The family roster exceeds the selected package capacity.");
            }
            if (participation.IsInitialPurchaseOrReactivation && participation.QualifyingParticipantCount < 2)
            {
                if (consumedManagerExemption) group.ManagerIndividualSeatExempt = true;
                return Error.Conflict("family.min_members", "Family activation requires two funded participants.");
            }

            var today = BusinessCalendarDate.KuwaitToday(_clock.UtcNow);
            if (request.StartDate < today)
                return Error.Conflict("family.renewal_dates_overlap");
            var requestedServiceDates = _schedule.GetServiceDates(request.StartDate, previous.DurationDays, previous.IncludeFriday);
            if (requestedServiceDates.Count == 0 || requestedServiceDates[0] <= previous.EndDate)
                return Error.Conflict("family.renewal_dates_overlap");
            var requestedLastServiceDate = requestedServiceDates[requestedServiceDates.Count - 1];

            var anotherLive = await _db.Subscriptions.AsNoTracking().AnyAsync(x => x.FamilyGroupId == groupId
                && x.FamilyMemberId == memberId && x.Id != previous.Id
                && new[] { SubscriptionStatus.Paid, SubscriptionStatus.Active, SubscriptionStatus.Scheduled,
                    SubscriptionStatus.Frozen, SubscriptionStatus.SuspendedForReview }.Contains(x.Status)
                && x.StartDate <= requestedLastServiceDate && x.EndDate >= requestedServiceDates[0], ct);
            if (anotherLive) return Error.Conflict("family.renewal_dates_overlap");

            if (group.Members.First(x => x.Id == memberId).MemberSubscriptionId != previous.Id)
                return Error.Conflict("family.renewal_pointer_changed");
            var operational = await MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalPolicyRules.ReadAsync(_db, ct);
            if (!MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalPolicyRules.IsRenewalAvailable(operational, previous.EndDate, today))
                return Error.Conflict("subscription.renewal_not_available");
            var schedule = _schedule.TryResolveCheckoutStart(request.StartDate, previous.IncludeFriday, out _, out var startError);
            if (!schedule) return Error.Validation(startError ?? "checkout.start_date_invalid");

            var customerUserId = previous.CustomerUserId;
            if (!await _db.CustomerProfiles.AsNoTracking().AnyAsync(x => x.UserId == customerUserId
                && x.AllergiesCompletedAtUtc != null, ct))
                return Error.Conflict("family.allergies_incomplete");
            var address = await _db.CustomerAddresses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.AddressId
                && (x.UserId == actorId || x.UserId == customerUserId), ct);
            if (address?.RegionId is not Guid regionId || string.IsNullOrWhiteSpace(address.AddressLine)
                || !await _db.Regions.AnyAsync(x => x.Id == regionId && x.IsActive, ct))
                return Error.Validation("family.address_required");
            if (address.BlockRegionId.HasValue && !await _db.Regions.AnyAsync(x => x.Id == address.BlockRegionId
                && x.ParentRegionId == regionId && x.IsActive, ct))
                return Error.Validation("address.invalid_region");

            var pricing = await _sender.Send(new PreviewCustomerPricingQuery(previous.ProgramId, previous.BundleId,
                previous.SelectedTier, previous.DurationDays, IncludeFriday: previous.IncludeFriday,
                FridayDays: previous.IncludeFriday ? _schedule.GetFridayAddonDays(previous.DurationDays) : 0), ct);
            if (pricing.IsFailure) return pricing.Error;
            var otherPaidSubscriptions = await _db.Subscriptions.AsNoTracking().Where(x => x.FamilyGroupId == groupId
                && x.FamilyMemberId != memberId && x.Id != previous.Id
                && new[] { SubscriptionStatus.Paid, SubscriptionStatus.Active, SubscriptionStatus.Scheduled,
                    SubscriptionStatus.Frozen, SubscriptionStatus.SuspendedForReview }.Contains(x.Status)).ToListAsync(ct);
            var otherIds = otherPaidSubscriptions.Select(x => x.Id).ToList();
            var overlappingParticipantIds = otherIds.Count == 0 ? new HashSet<Guid>() : (await _db.SubscriptionDays.AsNoTracking()
                .Where(x => otherIds.Contains(x.SubscriptionId) && x.ServiceDate >= requestedServiceDates[0]
                    && x.ServiceDate <= requestedLastServiceDate && x.Status != SubscriptionDayStatus.Cancelled
                    && x.Status != SubscriptionDayStatus.Frozen)
                .Select(x => x.SubscriptionId).Distinct().ToListAsync(ct)).ToHashSet();
            var eligibleMemberIds = otherPaidSubscriptions.Where(x => x.FamilyMemberId.HasValue
                    && overlappingParticipantIds.Contains(x.Id)).Select(x => x.FamilyMemberId!.Value).ToHashSet();
            eligibleMemberIds.Remove(memberId);
            var settings = await _familyPricing.GetAsync(ct);
            var packageResult = FamilyPackagePolicy.Resolve(settings, group, eligibleMemberIds.Count, 1);
            if (packageResult.IsFailure) return packageResult.Error;
            var package = packageResult.Value.EffectivePackage;
            var discountPercent = group.DiscountPercentSnapshot;
            var currency = pricing.Value.CurrencyCode;
            var digits = FamilyPaymentAllocationService.CurrencyMinorDigits(currency);
            var gross = pricing.Value.FinalCustomerPrice;
            var discount = Math.Round(gross * discountPercent, digits, MidpointRounding.AwayFromZero);
            var net = gross - discount;
            if (gross <= 0m || net <= 0m) return Error.Conflict("family.renewal_quote_invalid");

            var contractError = FamilyInvoiceGate.ValidateNewInvoice(request.ClientContractVersion, _invoiceOptions);
            if (contractError is not null) return contractError;
            var method = await FamilyInvoiceGate.ResolvePaymentMethodAsync(_payments, net, paymentMethodId, currency, ct);
            if (method.IsFailure) return method.Error;

            var candidates = await _eligibility.EvaluateAsync(new RestaurantEligibilityRequest(previous.ProgramId,
                previous.BundleId, previous.SelectedTier, regionId, request.StartDate, previous.DurationDays,
                previous.IncludeFriday, customerUserId), ct);
            var restaurant = candidates.Where(x => x.IsEligible).OrderBy(x => x.NameEn).ThenBy(x => x.RestaurantId).FirstOrDefault();
            if (restaurant is null) return Error.Conflict("restaurant.not_eligible");

            await _capacityLock.AcquireAsync(restaurant.RestaurantId, requestedServiceDates, ct);
            await _locks.LockRestaurantCapacityAsync(requestedServiceDates.Select(x => (restaurant.RestaurantId, x)), ct);
            var capacity = await _capacityReader.ReadAsync(requestedServiceDates.Select(x => (restaurant.RestaurantId, x)).ToList(), ct);
            if (requestedServiceDates.Any(date => !capacity.TryGetValue((restaurant.RestaurantId, date), out var units)
                || units.LimitBoxes is not > 0 || units.TotalCommittedUnits + 1 > units.LimitBoxes))
                return Error.Conflict("family.capacity_unavailable");

            var create = await _sender.Send(new CreateSubscriptionCommand(customerUserId, previous.ProgramId,
                previous.BundleId, previous.SelectedTier, previous.DurationDays, request.StartDate,
                address.AddressLine, RegionId: regionId, CorrelationId: request.CorrelationId,
                IncludeFriday: previous.IncludeFriday), ct);
            if (create.IsFailure) return create.Error;
            var next = await _db.Subscriptions.FirstAsync(x => x.Id == create.Value.Id, ct);
            next.FamilyGroupId = groupId; next.FamilyMemberId = memberId;
            next.PaidAmount = new Money(net, currency);

            var batch = new FamilyCheckoutBatch
            {
                FamilyGroupId = groupId, PayerCustomerUserId = actorId,
                IdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    ? $"renewal:{renewalRequestId:N}" : $"renewal:{renewalRequestId:N}:{request.IdempotencyKey.Trim()}",
                Status = FamilyCheckoutBatchStatus.PaymentPending, Purpose = "Renewal",
                RenewalRequestId = renewalRequestId, PackageId = package.Id, PackageNameAr = package.NameAr,
                PackageNameEn = package.NameEn, PackageMinMembers = package.MinMembers, PackageMaxMembers = package.MaxMembers,
                FamilyDiscountPercent = discountPercent, CurrencyCode = currency, Subtotal = gross,
                FamilyDiscountAmount = discount, FinalPrice = net, ExpiresAtUtc = _clock.UtcNow.AddMinutes(15),
                PaymentAnchorSubscriptionId = next.Id, CreatedAtUtc = _clock.UtcNow,
            };
            batch.Lines.Add(new FamilyCheckoutBatchLine
            {
                Batch = batch, FamilyMemberId = memberId, SubscriptionId = next.Id, CheckoutSelected = true,
                RenewalRequestId = renewalRequestId, PreviousSubscriptionId = previous.Id,
                ProgramId = previous.ProgramId, BundleId = previous.BundleId, Tier = previous.SelectedTier,
                DurationDays = previous.DurationDays, IncludeFriday = previous.IncludeFriday, StartDate = request.StartDate,
                DeliveryAddressId = address.Id, AddressLabel = address.Label, AddressLine = address.AddressLine,
                RegionId = regionId, BlockRegionId = address.BlockRegionId, Governorate = address.Governorate,
                Area = address.Area, Block = address.Block, Street = address.Street, Building = address.Building,
                Unit = address.Unit, Floor = address.Floor, ResolvedRestaurantId = restaurant.RestaurantId,
                MenuPlanVersionsJson = JsonSerializer.Serialize(restaurant.MenuPlanVersionsByDate),
                PriceBeforeFamilyDiscount = gross, FamilyDiscountAllocated = discount, NetPaidAmount = net,
                CurrencyCode = currency, CreatedAtUtc = _clock.UtcNow,
            });
            var attempt = new FamilyCheckoutPaymentAttempt
            {
                Batch = batch, AttemptKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    ? renewalRequestId.ToString("N") : request.IdempotencyKey.Trim(),
                RequestedAtUtc = _clock.UtcNow, CreatedAtUtc = _clock.UtcNow, ResultCode = "Initializing",
            };
            batch.PaymentAttempts.Add(attempt);
            _db.FamilyCheckoutBatches.Add(batch);
            renewal.Status = "PaymentPending"; renewal.FamilyGroupId = groupId; renewal.FamilyMemberId = memberId;
            renewal.RequestedStartDate = request.StartDate; renewal.RequestedAddressId = address.Id;
            renewal.CheckoutFingerprint = checkoutFingerprint;
            renewal.BatchId = batch.Id; renewal.BatchVersion = batch.Version;
            foreach (var resolved in restaurant.MenuPlanVersionsByDate)
                _db.CapacityHolds.Add(new CapacityHold { RestaurantId = restaurant.RestaurantId, SubscriptionId = next.Id,
                    MenuPlanVersionId = resolved.MenuPlanVersionId, ServiceDate = resolved.ServiceDate, Quantity = 1,
                    Status = CapacityHoldStatus.PendingPayment, ExpiresAtUtc = batch.ExpiresAtUtc.Value });
            await _db.SaveChangesAsync(ct);

            return new FamilyReservationOutcome(groupId, next.Id, [next.Id], batch.ExpiresAtUtc.Value, net,
                [new PendingPoolCapture(next.Id, candidates.Where(x => x.IsEligible).ToList(), restaurant.RestaurantId)],
                batch.Id, batch.Version, attempt.Id, true);
        }, cancellationToken);

        if (reservationResult.IsFailure) return reservationResult.Error;
        var reservation = reservationResult.Value;
        if (reservation.FailureCode is not null) return Error.Conflict(reservation.FailureCode);
        if (reservation.PaymentUrl is { Length: > 0 } savedUrl) return Started(reservation, savedUrl);
        foreach (var capture in reservation.PoolCaptures)
        {
            var captured = await _restaurantPool.CaptureAsync(capture.SubscriptionId, capture.Eligible,
                capture.SelectedRestaurantId, cancellationToken);
            if (!captured.IsSuccess)
            {
                await _transactions.ExecuteResultAsync<bool>(async ct =>
                {
                    await _locks.LockFamilyGroupAsync(groupId, ct);
                    await _locks.LockFamilyBatchAsync(reservation.BatchId, ct);
                    var batch = await _db.FamilyCheckoutBatches.SingleAsync(x => x.Id == reservation.BatchId, ct);
                    var renewal = await _db.SubscriptionRenewals.SingleAsync(x => x.Id == renewalRequestId, ct);
                    batch.Status = FamilyCheckoutBatchStatus.ReviewRequired; batch.Version++;
                    renewal.Status = "ReviewRequired";
                    await _db.SaveChangesAsync(ct);
                    return true;
                }, cancellationToken);
                return Error.Conflict(captured.ErrorCode ?? "checkout.restaurant_pool_failed");
            }
        }
        var payer = await _users.FindByIdAsync(actorId, cancellationToken);
        if (payer is null) return Error.Validation("checkout.customer_not_found");
        try
        {
            var receipt = await _payments.ExecutePaymentAsync(reservation.FinalPrice, payer.FullName ?? "", payer.Email ?? "",
                payer.PhoneNumber ?? "", request.CallbackUrl, request.ErrorUrl, groupId.ToString(), paymentMethodId, cancellationToken);
            if (string.IsNullOrWhiteSpace(receipt.PaymentId) || string.IsNullOrWhiteSpace(receipt.TrackId)
                || string.IsNullOrWhiteSpace(receipt.PaymentUrl))
                throw new FamilyCheckoutReservationException("payment.initialization_failed");
            return await _transactions.ExecuteAsync<Result<FamilyCheckoutStartedDto>>(async ct =>
            {
                await _locks.LockFamilyGroupAsync(groupId, ct);
                await _locks.LockFamilyBatchAsync(reservation.BatchId, ct);
                var batch = await _db.FamilyCheckoutBatches.SingleAsync(x => x.Id == reservation.BatchId, ct);
                var attempt = await _db.FamilyCheckoutPaymentAttempts.SingleAsync(x => x.Id == reservation.AttemptId, ct);
                attempt.GatewayPaymentId = receipt.PaymentHandle; attempt.GatewayPaymentUrl = receipt.PaymentUrl;
                attempt.ResultCode = "Initialized"; attempt.Version++;
                batch.GatewayPaymentId = receipt.PaymentHandle;
                if (batch.Purpose == "Renewal" && batch.RenewalRequestId is Guid renewalId)
                {
                    var renewal = await _db.SubscriptionRenewals.SingleOrDefaultAsync(x => x.Id == renewalId
                        && x.BatchId == batch.Id && x.Status == "PaymentPending", ct);
                    var line = await _db.FamilyCheckoutBatchLines.SingleOrDefaultAsync(x => x.BatchId == batch.Id
                        && x.CheckoutSelected && x.RenewalRequestId == renewalId, ct);
                    var oldSubscription = line?.PreviousSubscriptionId is Guid oldId
                        ? await _db.Subscriptions.SingleOrDefaultAsync(x => x.Id == oldId, ct) : null;
                    var nextSubscription = line?.SubscriptionId is Guid nextId
                        ? await _db.Subscriptions.SingleOrDefaultAsync(x => x.Id == nextId, ct) : null;
                    var familyMember = line is null ? null : await _db.FamilyMembers.SingleOrDefaultAsync(x => x.Id == line.FamilyMemberId
                        && x.FamilyGroupId == groupId && !x.IsDeleted, ct);
                    var supportedOldStatuses = new[] { SubscriptionStatus.Paid, SubscriptionStatus.Active,
                        SubscriptionStatus.Scheduled, SubscriptionStatus.Frozen, SubscriptionStatus.SuspendedForReview };
                    var validRenewal = renewal is not null && line is not null && oldSubscription is not null
                        && nextSubscription is not null && familyMember is not null
                        && familyMember.MemberSubscriptionId == oldSubscription.Id
                        && oldSubscription.FamilyGroupId == groupId && oldSubscription.FamilyMemberId == line.FamilyMemberId
                        && supportedOldStatuses.Contains(oldSubscription.Status)
                        && nextSubscription.FamilyGroupId == groupId && nextSubscription.FamilyMemberId == line.FamilyMemberId
                        && nextSubscription.Status == SubscriptionStatus.PendingPayment
                        && nextSubscription.StartDate > oldSubscription.EndDate
                        && renewal.RequestedStartDate == nextSubscription.StartDate
                        && renewal.RequestedAddressId == line.DeliveryAddressId
                        && nextSubscription.ProgramId == oldSubscription.ProgramId
                        && nextSubscription.BundleId == oldSubscription.BundleId
                        && nextSubscription.SelectedTier == oldSubscription.SelectedTier
                        && nextSubscription.DurationDays == oldSubscription.DurationDays
                        && nextSubscription.IncludeFriday == oldSubscription.IncludeFriday;
                    if (!validRenewal)
                    {
                        batch.Status = FamilyCheckoutBatchStatus.ReviewRequired; batch.Version++;
                        attempt.ResultCode = "ReviewRequired"; attempt.Version++;
                        if (renewal is not null) renewal.Status = "ReviewRequired";
                        await _db.SaveChangesAsync(ct);
                        return Error.Conflict("family.renewal_intent_changed");
                    }
                }
                if (batch.Status != FamilyCheckoutBatchStatus.PaymentPending)
                {
                    batch.Status = FamilyCheckoutBatchStatus.ReviewRequired;
                    await _db.SaveChangesAsync(ct);
                    return Error.Conflict("family.batch_review_required");
                }
                await _db.SaveChangesAsync(ct);
                return Started(reservation, attempt.GatewayPaymentUrl);
            }, cancellationToken);
        }
        catch (Exception)
        {
            if (_db is DbContext context) context.ChangeTracker.Clear();
            await _transactions.ExecuteAsync(async ct =>
            {
                await _locks.LockFamilyGroupAsync(groupId, ct);
                await _locks.LockFamilyBatchAsync(reservation.BatchId, ct);
                var batch = await _db.FamilyCheckoutBatches.SingleAsync(x => x.Id == reservation.BatchId, ct);
                var renewal = await _db.SubscriptionRenewals.SingleAsync(x => x.Id == renewalRequestId, ct);
                batch.Status = FamilyCheckoutBatchStatus.ReviewRequired; renewal.Status = "ReviewRequired";
                var attempt = await _db.FamilyCheckoutPaymentAttempts.SingleAsync(x => x.Id == reservation.AttemptId, ct);
                attempt.ResultCode = "InitializationUncertain"; await _db.SaveChangesAsync(ct);
                return true;
            }, CancellationToken.None);
            return Error.Conflict("family.batch_review_required");
        }
    }

    public async Task<Result<FamilyCheckoutStartedDto>> StartAsync(FamilyStartCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserType != UserType.Customer || _currentUser.UserId is not Guid managerId)
            return Error.Forbidden("auth.forbidden", "auth.forbidden");
        var groupId = await _db.FamilyGroups.AsNoTracking().Where(x => x.ManagerCustomerUserId == managerId
            && (x.Status == FamilyGroupStatus.Draft || x.Status == FamilyGroupStatus.CheckoutPending || x.Status == FamilyGroupStatus.Active))
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
        if (groupId is null) return Error.NotFound("family.not_found", "family.not_found");
        Result<FamilyReservationOutcome> prepared;
        try
        {
            prepared = await _transactions.ExecuteAsync(async ct =>
            {
                await _locks.LockFamilyGroupAsync(groupId.Value, ct);
                return await PrepareAsync(request, ct);
            }, cancellationToken);
        }
        catch (FamilyCheckoutReservationException ex)
        {
            if (_db is DbContext context) context.ChangeTracker.Clear();
            return Error.Conflict(ex.Code, ex.Code);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (_db is DbContext context) context.ChangeTracker.Clear();
            return Error.Conflict("family.coupon_limit_reached", "The coupon is no longer available or usage limit was reached.");
        }
        if (prepared.IsFailure) return prepared.Error;
        var reservation = prepared.Value;
        if (!reservation.OwnsInitialization)
        {
            // Another request owns the durable attempt. Wait for its saved result;
            // a crash leaves Initializing and never authorizes a second invoice.
            for (var i = 0; i < 40; i++)
            {
                var saved = await _db.FamilyCheckoutPaymentAttempts.AsNoTracking().SingleAsync(x => x.Id == reservation.AttemptId, cancellationToken);
                if (saved.ResultCode is "Initialized" or "Paid" && !string.IsNullOrWhiteSpace(saved.GatewayPaymentUrl))
                    return Started(reservation, saved.GatewayPaymentUrl);
                if (saved.ResultCode != "Initializing")
                    return Error.Conflict("family.batch_review_required", "family.batch_review_required");
                await Task.Delay(100, cancellationToken);
            }
            return Error.Conflict("family.payment_initializing", "family.payment_initializing");
        }
        try
        {
            var user = await _users.FindByIdAsync(managerId, cancellationToken)
                ?? throw new FamilyCheckoutReservationException("checkout.customer_not_found");
            var receipt = await _payments.ExecutePaymentAsync(reservation.FinalPrice, user.FullName ?? "", user.Email ?? "",
                user.PhoneNumber ?? "", request.CallbackUrl, request.ErrorUrl, groupId.Value.ToString(),
                request.PaymentMethodId <= 0 ? 3 : request.PaymentMethodId, cancellationToken);
            if (string.IsNullOrWhiteSpace(receipt.PaymentId) || string.IsNullOrWhiteSpace(receipt.TrackId)
                || string.IsNullOrWhiteSpace(receipt.PaymentUrl))
                throw new FamilyCheckoutReservationException("payment.initialization_failed");
            return await _transactions.ExecuteAsync<Result<FamilyCheckoutStartedDto>>(async ct =>
            {
                await _locks.LockFamilyGroupAsync(groupId.Value, ct);
                var batch = await _db.FamilyCheckoutBatches.SingleAsync(x => x.Id == reservation.BatchId, ct);
                var attempt = await _db.FamilyCheckoutPaymentAttempts.SingleAsync(x => x.Id == reservation.AttemptId, ct);
                // Always persist the real receipt so even a delayed gateway response can
                // be resolved. A batch closed while awaiting the gateway goes to review.
                attempt.GatewayPaymentId = receipt.PaymentHandle; attempt.GatewayPaymentUrl = receipt.PaymentUrl;
                attempt.ResultCode = "Initialized"; attempt.Version++;
                batch.GatewayPaymentId = receipt.PaymentHandle;
                if (batch.Status != FamilyCheckoutBatchStatus.PaymentPending)
                {
                    batch.Status = FamilyCheckoutBatchStatus.ReviewRequired;
                    await _db.SaveChangesAsync(ct);
                    return Error.Conflict("family.batch_review_required", "family.batch_review_required");
                }
                var group = await _db.FamilyGroups.SingleAsync(x => x.Id == groupId, ct);
                group.PaymentId = receipt.PaymentHandle;
                var anchor = await _db.Subscriptions.SingleAsync(x => x.Id == reservation.PaymentAnchorSubscriptionId, ct);
                anchor.PaymentId = receipt.PaymentHandle;
                await _db.SaveChangesAsync(ct);
                return Started(reservation, attempt.GatewayPaymentUrl);
            }, cancellationToken);
        }
        catch (Exception)
        {
            // Product decision: an initialization failure (including a timeout)
            // never forces a review stop. The batch is marked Failed and the
            // group returns to Draft so the customer can retry the payment
            // immediately, without waiting for the 15-minute expiry worker.
            if (_db is DbContext context) context.ChangeTracker.Clear();
            await _transactions.ExecuteAsync(async ct =>
            {
                await _locks.LockFamilyGroupAsync(groupId.Value, ct);
                var batch = await _db.FamilyCheckoutBatches.Include(x => x.Lines)
                    .SingleAsync(x => x.Id == reservation.BatchId, ct);
                var attempt = await _db.FamilyCheckoutPaymentAttempts.SingleAsync(x => x.Id == reservation.AttemptId, ct);
                batch.Status = FamilyCheckoutBatchStatus.Failed;
                attempt.ResultCode = "Failed";
                attempt.Version++;

                // Cancel this attempt's abandoned reservations and release their
                // capacity holds — mirroring the hold-expiry worker — so the
                // retry is not blocked by stale PendingPayment rows.
                var reservedIds = batch.Lines.Where(x => x.SubscriptionId is not null)
                    .Select(x => x.SubscriptionId!.Value).ToList();
                var reservedSubs = await _db.Subscriptions
                    .Where(x => reservedIds.Contains(x.Id) && x.Status == SubscriptionStatus.PendingPayment)
                    .ToListAsync(ct);
                foreach (var sub in reservedSubs)
                {
                    var holds = await _db.CapacityHolds
                        .Where(x => x.SubscriptionId == sub.Id && x.Status == CapacityHoldStatus.PendingPayment)
                        .ToListAsync(ct);
                    if (holds.Count > 0)
                        await _capacityLock.AcquireAsync(holds[0].RestaurantId,
                            holds.Select(x => x.ServiceDate).ToList(), ct);
                    foreach (var hold in holds)
                    {
                        hold.Status = CapacityHoldStatus.Released;
                        hold.ReleasedAtUtc = _clock.UtcNow;
                    }
                    if (_coupons is not null)
                        await _coupons.ReleaseCheckoutRedemptionAsync(sub.Id, ct);
                    var oldStatus = sub.Status;
                    sub.Status = SubscriptionStatus.Cancelled;
                    sub.LastOperationAtUtc = _clock.UtcNow;
                    _db.SubscriptionOperations.Add(new SubscriptionOperation
                    {
                        SubscriptionId = sub.Id,
                        OperationType = "CHECKOUT_INITIALIZATION_FAILED",
                        PreviousStatus = oldStatus,
                        NewStatus = SubscriptionStatus.Cancelled,
                        Reason = "Payment initialization failed; the reservation was released for immediate retry.",
                        PerformedByUserId = Guid.Empty,
                        PerformedByUserType = UserType.Admin,
                        TimestampUtc = _clock.UtcNow,
                    });
                }

                var reverted = await _db.FamilyGroups.SingleAsync(x => x.Id == groupId.Value, ct);
                if (reverted.Status == FamilyGroupStatus.CheckoutPending)
                {
                    reverted.Status = FamilyGroupStatus.Draft;
                    reverted.PaymentAnchorSubscriptionId = null;
                    reverted.PaymentId = null;
                    reverted.PaymentInvoiceId = null;
                }
                await _db.SaveChangesAsync(ct);
                return true;
            }, CancellationToken.None);
            return Error.Conflict("payment.initialization_failed", "payment.initialization_failed");
        }
    }

    private static FamilyCheckoutStartedDto Started(FamilyReservationOutcome reservation, string url)
        => new(reservation.FamilyGroupId, reservation.PaymentAnchorSubscriptionId, url, reservation.ExpiresAtUtc,
            reservation.FinalPrice, reservation.BatchId, reservation.BatchVersion);

    private async Task<Result<FamilyReservationOutcome>> PrepareAsync(
        FamilyStartCheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserType != UserType.Customer || _currentUser.UserId is not Guid managerId)
            return Error.Forbidden("auth.forbidden", "A customer account is required.");

        var expiredCount = 0;

        var group = await _db.FamilyGroups
            .Include(x => x.Members)
                .ThenInclude(x => x.MemberSubscription)
            .FirstOrDefaultAsync(
                x => x.ManagerCustomerUserId == managerId
                    && (x.Status == FamilyGroupStatus.Draft
                        || x.Status == FamilyGroupStatus.CheckoutPending
                        || x.Status == FamilyGroupStatus.Active),
                cancellationToken);

        if (group is null)
        {
            var cutoff = FamilyDraftExpiry.LookbackUtc(_clock.UtcNow);
            var recentlyExpired = expiredCount > 0
                || (cutoff is DateTime since
                    && await _db.FamilyGroups.IgnoreQueryFilters().AsNoTracking()
                        .AnyAsync(
                            g => g.ManagerCustomerUserId == managerId
                                 && g.IsDeleted
                                 && g.DeletedAtUtc != null
                                 && g.DeletedAtUtc >= since,
                            cancellationToken));
            return recentlyExpired
                ? Error.Conflict(MessageCodes.Family.SessionExpired, FamilyDraftExpiry.SessionEndedMessageAr)
                : Error.NotFound("family.not_found", "No family group was found.");
        }

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var historical = await _db.FamilyCheckoutBatches.AsNoTracking().Include(x => x.Lines).Include(x => x.PaymentAttempts)
                .SingleOrDefaultAsync(x => x.FamilyGroupId == group.Id && x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (historical is not null && historical.Purpose != "Initial")
                return Error.Conflict("family.checkout_pending", "family.checkout_pending");
            if (historical is not null && historical.Status is not (FamilyCheckoutBatchStatus.Draft or FamilyCheckoutBatchStatus.PaymentPending))
            {
                var recorded = historical.PaymentAttempts.SingleOrDefault();
                if (historical.Status == FamilyCheckoutBatchStatus.Paid && historical.PaymentAnchorSubscriptionId is Guid anchor
                    && recorded?.GatewayPaymentUrl is not null)
                    return new FamilyReservationOutcome(group.Id, anchor,
                        historical.Lines.Where(x => x.SubscriptionId != null).Select(x => x.SubscriptionId!.Value).ToList(),
                        historical.ExpiresAtUtc ?? recorded.RequestedAtUtc, historical.FinalPrice, [], historical.Id,
                        historical.Version, recorded.Id, false);
                return Error.Conflict(historical.Status == FamilyCheckoutBatchStatus.ReviewRequired
                    ? "family.batch_review_required" : "family.batch_closed", "family.batch_closed");
            }
        }

        await FamilyDraftExpiry.ExpireDueAsync(_db, _clock.UtcNow, cancellationToken, managerId, groupLockHeld: true);
        var frozenBatch = await _db.FamilyCheckoutBatches.Include(x => x.Lines).Include(x => x.PaymentAttempts)
            .Where(x => x.FamilyGroupId == group.Id && (x.Status == FamilyCheckoutBatchStatus.PaymentPending
                || x.Status == FamilyCheckoutBatchStatus.ReviewRequired))
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (frozenBatch is not null && frozenBatch.Purpose != "Initial")
            return Error.Conflict("family.checkout_pending", "family.checkout_pending");
        if (frozenBatch?.Status == FamilyCheckoutBatchStatus.ReviewRequired)
            return Error.Conflict("family.batch_review_required", "family.batch_review_required");
        if (frozenBatch is not null)
        {
            if (request.BatchId is Guid requested && requested != frozenBatch.Id)
                return Error.Conflict("family.quote_changed", "family.quote_changed");
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey) && request.IdempotencyKey != frozenBatch.IdempotencyKey)
                return Error.Conflict("family.checkout_pending", "family.checkout_pending");
            var recorded = frozenBatch.PaymentAttempts.SingleOrDefault();
            if (recorded is null || frozenBatch.PaymentAnchorSubscriptionId is not Guid recordedAnchor)
                return Error.Conflict("family.batch_review_required", "family.batch_review_required");
            return new FamilyReservationOutcome(group.Id, recordedAnchor,
                frozenBatch.Lines.Where(x => x.SubscriptionId != null).Select(x => x.SubscriptionId!.Value).ToList(),
                frozenBatch.ExpiresAtUtc ?? recorded.RequestedAtUtc, frozenBatch.FinalPrice, [],
                frozenBatch.Id, frozenBatch.Version, recorded.Id, false);
        }

        var pendingPayInFlight = await _db.Subscriptions.AsNoTracking()
            .AnyAsync(x => x.FamilyGroupId == group.Id && x.Status == SubscriptionStatus.PendingPayment, cancellationToken);
        if (group.Status == FamilyGroupStatus.CheckoutPending || pendingPayInFlight)
            return Error.Conflict("family.batch_review_required", "family.batch_review_required");

        var activeIds = await FamilyCheckoutGate.ActiveAccessMemberIdsAsync(_db, group, cancellationToken);
        var selectedIds = await FamilyCheckoutSelectionService.GetSelectedMemberIdsOrNullAsync(_db, group.Id, cancellationToken);
        FamilyCheckoutBatch? draft = null;
        if (selectedIds is null)
        {
            // No draft batch has ever been materialized: no explicit selection
            // exists. Apply the same default the summary/current screens display —
            // every active, unsubscribed member with a pricable plan subscribes
            // now until the customer explicitly defers one.
            var defaultMembers = group.Members
                .Where(x => activeIds.Contains(x.Id) && x.MemberSubscriptionId is null
                    && FamilyMemberPlanRules.HasPricablePlan(x))
                .ToList();
            if (defaultMembers.Count == 0)
                return Error.Conflict("family.checkout_no_selection", "family.checkout_no_selection");
            selectedIds = defaultMembers.Select(x => x.Id).ToHashSet();
            draft = await FamilyCheckoutSelectionService.EnsureDraftBatchAsync(
                _db, group, managerId, defaultMembers, _clock.UtcNow, cancellationToken);
        }
        if (selectedIds.Count == 0)
            return Error.Conflict("family.checkout_no_selection", "family.checkout_no_selection");
        var members = group.Members.Where(x => activeIds.Contains(x.Id))
            .OrderBy(x => x.Id).ToList();
        var participationReader = new FamilyParticipationContextReader(_db, _clock, _familyPricing);
        var participation = await participationReader.GetAsync(group, selectedIds, cancellationToken);
        if (members.Any(x => selectedIds.Contains(x.Id)
            && !participation.LiveFamilyMemberIds.Contains(x.Id)
            && !FamilyMemberPlanRules.HasPricablePlan(x)
            && !(x.Role == FamilyMemberRole.Manager && participation.ManagerSeatExempt
                && participation.ManagerIndividualIsLive)))
            return Error.Conflict("family.member_incomplete", "family.member_incomplete");
        var payableMembers = members.Where(x => selectedIds.Contains(x.Id)
            && participation.PurchasableMemberIds.Contains(x.Id)).ToList();
        if (payableMembers.Count == 0)
            return Error.Conflict("family.checkout_no_selection", "family.checkout_no_selection");
        if (payableMembers.Any(x => !FamilyMemberPlanRules.HasPricablePlan(x)))
            return Error.Conflict("family.member_incomplete", "family.member_incomplete");
        draft ??= await _db.FamilyCheckoutBatches.Include(x => x.Lines)
            .SingleAsync(x => x.FamilyGroupId == group.Id && x.Status == FamilyCheckoutBatchStatus.Draft, cancellationToken);
        if ((request.BatchId.HasValue && request.BatchId != draft.Id)
            || (request.BatchVersion.HasValue && request.BatchVersion != draft.Version))
            return Error.Conflict("family.quote_changed", "family.quote_changed");
        var quoteResult = await _quote.QuoteAsync(group, cancellationToken,
            new FamilyQuoteScope(activeIds, selectedIds, request.PromoCode, managerId));
        if (quoteResult.IsFailure) return quoteResult.Error;
        var quote = quoteResult.Value;
        var allergiesCompleted = await FamilyCheckoutAllergyLookup.CompletedMemberIdsAsync(_db, group, payableMembers, cancellationToken);
        var (canCheckout, blockReason) = FamilyCheckoutGate.Evaluate(group, payableMembers, quote, allergiesCompleted);
        if (!canCheckout)
            return Error.Conflict(blockReason ?? "family.checkout_blocked", blockReason ?? "family.checkout_blocked");

        if (quote.FinalPrice > 0m)
        {
            var contractError = FamilyInvoiceGate.ValidateNewInvoice(request.ClientContractVersion, _invoiceOptions);
            if (contractError is not null) return contractError;
            var method = await FamilyInvoiceGate.ResolvePaymentMethodAsync(_payments, quote.FinalPrice,
                request.PaymentMethodId <= 0 ? 3 : request.PaymentMethodId, quote.CurrencyCode, cancellationToken);
            if (method.IsFailure) return method.Error;
        }

        await UnlinkPaymentFailedLeftoversAsync(group.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            if (await _db.FamilyCheckoutBatches.AnyAsync(x => x.FamilyGroupId == group.Id
                && x.Id != draft.Id && x.IdempotencyKey == request.IdempotencyKey, cancellationToken))
                return Error.Conflict("family.idempotency_key_used", "family.idempotency_key_used");
            draft.IdempotencyKey = request.IdempotencyKey;
        }

        var user = await _users.FindByIdAsync(managerId, cancellationToken);
        if (user is null)
            return Error.Validation("checkout.customer_not_found", "Customer account was not found.");

        var allocationByMember = new FamilyPaymentAllocationService().Allocate(quote.Lines, quote.FinalPrice,
            FamilyPaymentAllocationService.CurrencyMinorDigits(quote.CurrencyCode)).ToDictionary(x => x.MemberId);
        var priceByMember = allocationByMember.ToDictionary(x => x.Key, x => x.Value.NetPaidAmount);
        var memberIds = members.Select(m => m.Id).ToList();
        var accesses = await _db.FamilyMemberAccesses.AsNoTracking()
            .Where(a => memberIds.Contains(a.FamilyMemberId) && a.RemovedAtUtc == null)
            .ToListAsync(cancellationToken);
        var previousStatus = group.Status;
        var prepared = new List<PreparedMemberReservation>();
        foreach (var member in payableMembers)
        {
            if (member.MemberSubscriptionId is not null && !participation.PurchasableMemberIds.Contains(member.Id))
                continue;
            if (!FamilyMemberPlanRules.HasPricablePlan(member))
                continue;

            var customerUserId = ResolveMemberCustomerUserId(member, managerId, accesses);
            if (customerUserId is null)
                return Error.Conflict("family.member_account_required", "Family member must be linked before checkout.");

            var address = await ResolveMemberCheckoutAddressAsync(member, customerUserId.Value, managerId, cancellationToken);
            if (address is null)
                return Error.Validation("family.address_required", "Each payable family member needs a delivery address.");

            if (member.StartDate is not DateOnly chosenStartDate)
                return Error.Validation("family.start_date_required", "Choose a subscription start date for every paying family member.");
            var startDate = _schedule.ResolveFirstDeliveryDate(chosenStartDate, member.IncludeFriday);
            if (startDate == default) startDate = chosenStartDate;
            if (address.RegionId is null || string.IsNullOrWhiteSpace(address.AddressLine))
                return Error.Validation("family.address_required", "family.address_required");
            var regionId = address.RegionId ?? Guid.Empty;
            var serviceDates = _schedule.GetServiceDates(startDate, member.DurationDays!.Value, member.IncludeFriday);
            var allRows = await _eligibility.EvaluateAsync(
                new RestaurantEligibilityRequest(
                    member.ProgramId!.Value,
                    member.BundleId!.Value,
                    member.SelectedTier!.Value,
                    regionId,
                    startDate,
                    member.DurationDays.Value,
                    member.IncludeFriday,
                    customerUserId.Value),
                cancellationToken);
            var eligible = allRows.Where(x => x.IsEligible).OrderBy(x => x.NameEn).ThenBy(x => x.RestaurantId).ToList();
            var restaurant = eligible.FirstOrDefault();
            if (restaurant is null)
                return Error.Validation("restaurant.not_eligible", "No eligible restaurant is available for a family member.");

            var resolvedRegionId = regionId;
            prepared.Add(new PreparedMemberReservation(
                member,
                customerUserId.Value,
                address,
                resolvedRegionId,
                eligible,
                restaurant,
                startDate,
                serviceDates));
        }

        FamilyReservationOutcome reservation;
        try
        {
            reservation = await _transactions.ExecuteAsync(async ct =>
            {
                var expiresAt = _clock.UtcNow.AddMinutes(15);
                var created = new List<(FamilyMember Member, Subscription Subscription)>();

                var poolCaptures = new List<PendingPoolCapture>();
                foreach (var plan in prepared)
                {
                    var member = members.First(x => x.Id == plan.Member.Id);
                    await _capacityLock.AcquireAsync(plan.Restaurant.RestaurantId, plan.ServiceDates, ct);

                    // The per-member eligibility ran before any lock; re-check through the
                    // unified reader under the lock. Earlier members' holds are already
                    // saved in this transaction, so the check is aggregated family demand;
                    // any failure throws and the whole group rolls back (G08/G09).
                    var capacityUnits = await _capacityReader.ReadAsync(
                        plan.ServiceDates.Distinct().Select(date => (plan.Restaurant.RestaurantId, date)).ToList(), ct);
                    foreach (var date in plan.ServiceDates.Distinct())
                    {
                        var units = capacityUnits[(plan.Restaurant.RestaurantId, date)];
                        if (units.LimitBoxes is not { } limit || limit <= 0 || units.TotalCommittedUnits + 1 > limit)
                            throw new FamilyCheckoutReservationException("family.capacity_unavailable");
                    }

                    var create = await _sender.Send(new CreateSubscriptionCommand(
                        plan.CustomerUserId,
                        member.ProgramId!.Value,
                        member.BundleId!.Value,
                        member.SelectedTier!.Value,
                        member.DurationDays!.Value,
                        plan.StartDate,
                        plan.Address.AddressLine,
                        RegionId: plan.ResolvedRegionId,
                        CorrelationId: request.CorrelationId,
                        PromoCodeId: quote.CouponHolderMemberId == member.Id ? quote.CouponEvaluation?.PromoCodeId : null,
                        IncludeFriday: member.IncludeFriday), ct);
                    if (create.IsFailure)
                        throw new FamilyCheckoutReservationException(create.Error.Code);

                    var subscriptionEntity = await _db.Subscriptions.FirstAsync(x => x.Id == create.Value.Id, ct);
                    // The family reservation owns the normalized first-delivery
                    // date after the 72h lock plus 24h selection window.
                    subscriptionEntity.StartDate = plan.StartDate;
                    subscriptionEntity.EndDate = _schedule.GetEndDate(plan.StartDate, member.DurationDays!.Value, member.IncludeFriday);
                    if (!priceByMember.TryGetValue(member.Id, out var memberPrice))
                        throw new FamilyCheckoutReservationException("family.invalid_amount");

                    subscriptionEntity.PaidAmount = new Money(memberPrice, quote.CurrencyCode);
                    subscriptionEntity.FamilyGroupId = group.Id;
                    subscriptionEntity.FamilyMemberId = member.Id;
                    var line = draft.Lines.Single(x => x.FamilyMemberId == member.Id && x.CheckoutSelected);
                    var allocation = allocationByMember[member.Id];
                    line.SubscriptionId = subscriptionEntity.Id;
                    line.ProgramId = member.ProgramId; line.BundleId = member.BundleId;
                    line.Tier = member.SelectedTier; line.DurationDays = member.DurationDays;
                    line.IncludeFriday = member.IncludeFriday; line.StartDate = plan.StartDate;
                    line.DeliveryAddressId = plan.Address.Id; line.AddressLabel = plan.Address.Label;
                    line.AddressLine = plan.Address.AddressLine; line.RegionId = plan.ResolvedRegionId; line.BlockRegionId = plan.Address.BlockRegionId;
                    line.Governorate = plan.Address.Governorate; line.Area = plan.Address.Area;
                    line.Block = plan.Address.Block; line.Street = plan.Address.Street;
                    line.Building = plan.Address.Building; line.Unit = plan.Address.Unit; line.Floor = plan.Address.Floor;
                    line.ResolvedRestaurantId = plan.Restaurant.RestaurantId;
                    line.MenuPlanVersionsJson = JsonSerializer.Serialize(plan.Restaurant.MenuPlanVersionsByDate);
                    line.PriceBeforeFamilyDiscount = allocation.PriceBeforeFamilyDiscount;
                    line.FamilyDiscountAllocated = allocation.FamilyDiscountAllocated;
                    line.PromoCodeDiscountAllocated = allocation.PromoCodeDiscountAllocated;
                    line.NetPaidAmount = allocation.NetPaidAmount; line.CurrencyCode = quote.CurrencyCode;

                    foreach (var resolved in plan.Restaurant.MenuPlanVersionsByDate)
                    {
                        _db.CapacityHolds.Add(new CapacityHold
                        {
                            RestaurantId = plan.Restaurant.RestaurantId,
                            SubscriptionId = subscriptionEntity.Id,
                            MenuPlanVersionId = resolved.MenuPlanVersionId,
                            ServiceDate = resolved.ServiceDate,
                            Quantity = 1,
                            Status = CapacityHoldStatus.PendingPayment,
                            ExpiresAtUtc = expiresAt,
                        });
                    }

                    await _db.SaveChangesAsync(ct);
                    poolCaptures.Add(new PendingPoolCapture(
                        subscriptionEntity.Id, plan.Eligible, plan.Restaurant.RestaurantId));
                    created.Add((member, subscriptionEntity));
                }

                var payableCreated = created
                    .Where(x => x.Subscription.Status == SubscriptionStatus.PendingPayment)
                    .ToList();
                if (payableCreated.Count == 0)
                    throw new FamilyCheckoutReservationException("family.member_incomplete");

                foreach (var unused in draft.Lines.Where(x => !payableMembers.Any(m => m.Id == x.FamilyMemberId)).ToList())
                    _db.FamilyCheckoutBatchLines.Remove(unused);
                draft.Status = FamilyCheckoutBatchStatus.PaymentPending;
                draft.PackageId = quote.EffectiveFamilyPackageId;
                draft.PackageNameAr = quote.PackageNameAr;
                draft.PackageNameEn = quote.PackageNameEn;
                draft.PackageMinMembers = quote.EffectiveFamilyPackageMinMembers;
                draft.PackageMaxMembers = quote.EffectiveFamilyPackageMaxMembers;
                draft.FamilyDiscountPercent = quote.FamilyDiscountPercent;
                draft.CurrencyCode = quote.CurrencyCode; draft.Subtotal = quote.Subtotal;
                draft.FamilyDiscountAmount = quote.FamilyDiscountAmount; draft.FinalPrice = quote.FinalPrice;
                draft.ExpiresAtUtc = expiresAt; draft.Version++;
                var attempt = new FamilyCheckoutPaymentAttempt { BatchId = draft.Id, AttemptKey = draft.IdempotencyKey,
                    RequestedAtUtc = _clock.UtcNow, CreatedAtUtc = _clock.UtcNow, ResultCode = "Initializing" };
                _db.FamilyCheckoutPaymentAttempts.Add(attempt);
                var managerCreated = payableCreated.FirstOrDefault(x => x.Member.Role == FamilyMemberRole.Manager);
                var paymentAnchorId = managerCreated.Member is not null
                    ? managerCreated.Subscription.Id
                    : payableCreated[0].Subscription.Id;

                if (quote.CouponEvaluation is { IsValid: true, PromoCodeId: not null } familyCoupon
                    && quote.PromoCodeDiscountAmount > 0m)
                {
                    var holder = quote.CouponHolderMemberId is { } holderMember
                        ? payableCreated.First(x => x.Member.Id == holderMember)
                        : payableCreated.First(x => x.Subscription.Id == paymentAnchorId);
                    holder.Subscription.PromoCodeId = familyCoupon.PromoCodeId;
                    await _coupons.CreateCheckoutHoldAsync(familyCoupon, holder.Subscription.Id, managerId,
                        quote.PromoCodeDiscountAmount, ct);
                }

                draft.PaymentAnchorSubscriptionId = paymentAnchorId;
                group.DiscountPercentSnapshot = quote.FamilyDiscountPercent;
                group.QuotedFinalPrice = quote.FinalPrice;
                group.CurrencyCode = quote.CurrencyCode;
                if (previousStatus != FamilyGroupStatus.Active)
                    group.Status = FamilyGroupStatus.CheckoutPending;
                group.PaymentAnchorSubscriptionId = paymentAnchorId;
                group.PaymentId = null;
                group.PaymentInvoiceId = null;
                if (previousStatus != FamilyGroupStatus.Active)
                    group.PaidAtUtc = null;
                await _db.SaveChangesAsync(ct);

                return new FamilyReservationOutcome(
                    group.Id,
                    paymentAnchorId,
                    payableCreated.Select(x => x.Subscription.Id).ToList(),
                    expiresAt,
                    quote.FinalPrice,
                    poolCaptures, draft.Id, draft.Version, attempt.Id, true);
            }, cancellationToken);
        }
        catch (FamilyCheckoutReservationException) { throw; }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("family.coupon_limit_reached", "The coupon is no longer available or usage limit was reached.");
        }

        foreach (var capture in reservation.PoolCaptures)
        {
            var pool = await _restaurantPool.CaptureAsync(capture.SubscriptionId, capture.Eligible, capture.SelectedRestaurantId, cancellationToken);
            if (!pool.IsSuccess)
                throw new FamilyCheckoutReservationException(pool.ErrorCode ?? "checkout.restaurant_pool_failed");
        }
        return reservation;
    }

    private async Task UnlinkPaymentFailedLeftoversAsync(Guid familyGroupId, CancellationToken cancellationToken)
    {
        var leftovers = await _db.Subscriptions
            .Where(x => x.FamilyGroupId == familyGroupId && x.Status == SubscriptionStatus.PaymentFailed)
            .ToListAsync(cancellationToken);
        if (leftovers.Count == 0)
            return;

        var leftoverIds = leftovers.Select(x => x.Id).ToHashSet();
        var members = await _db.FamilyMembers
            .Where(x => x.FamilyGroupId == familyGroupId && x.MemberSubscriptionId != null)
            .ToListAsync(cancellationToken);
        foreach (var member in members)
        {
            if (member.MemberSubscriptionId is Guid subId && leftoverIds.Contains(subId))
                member.MemberSubscriptionId = null;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ReleaseReservationsAsync(IReadOnlyList<Guid> subscriptionIds, CancellationToken cancellationToken)
    {
        var subscriptions = await _db.Subscriptions
            .Where(x => subscriptionIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        foreach (var subscription in subscriptions)
        {
            subscription.Status = SubscriptionStatus.PaymentFailed;
            subscription.LastOperationAtUtc = _clock.UtcNow;
        }

        var holds = await _db.CapacityHolds
            .Where(x => subscriptionIds.Contains(x.SubscriptionId) && x.Status == CapacityHoldStatus.PendingPayment)
            .ToListAsync(cancellationToken);
        foreach (var hold in holds)
        {
            hold.Status = CapacityHoldStatus.Released;
            hold.ReleasedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static Guid? ResolveMemberCustomerUserId(
        FamilyMember member,
        Guid managerId,
        IReadOnlyList<FamilyMemberAccess> accesses)
    {
        var access = accesses.FirstOrDefault(a => a.FamilyMemberId == member.Id);
        if (access?.LinkedCustomerUserId is Guid linked)
            return linked;
        return member.Role == FamilyMemberRole.Manager ? managerId : null;
    }

    private async Task<CustomerAddress?> ResolveMemberCheckoutAddressAsync(
        FamilyMember member,
        Guid customerUserId,
        Guid managerId,
        CancellationToken ct)
    {
        if (member.DeliveryAddressId is Guid memberAddressId)
        {
            var assigned = await _db.CustomerAddresses.AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == memberAddressId && (x.UserId == customerUserId || x.UserId == managerId),
                    ct);
            if (assigned is not null)
                return assigned;
        }

        var owned = await _db.CustomerAddresses.AsNoTracking()
            .Where(x => x.UserId == customerUserId)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (owned is not null)
            return owned;

        return null;
    }

    private async Task RevertGroupAfterFailedPaymentAsync(
        Guid familyGroupId,
        FamilyGroupStatus previousStatus,
        CancellationToken cancellationToken)
    {
        var failedGroup = await _db.FamilyGroups.FirstAsync(x => x.Id == familyGroupId, cancellationToken);
        failedGroup.Status = previousStatus == FamilyGroupStatus.Active
            ? FamilyGroupStatus.Active
            : FamilyGroupStatus.Draft;
        failedGroup.PaymentAnchorSubscriptionId = previousStatus == FamilyGroupStatus.Active
            ? failedGroup.PaymentAnchorSubscriptionId
            : null;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private sealed record PreparedMemberReservation(
        FamilyMember Member,
        Guid CustomerUserId,
        CustomerAddress Address,
        Guid ResolvedRegionId,
        IReadOnlyList<RestaurantEligibilityDto> Eligible,
        RestaurantEligibilityDto Restaurant,
        DateOnly StartDate,
        IReadOnlyList<DateOnly> ServiceDates);

    private sealed record PendingPoolCapture(
        Guid SubscriptionId,
        IReadOnlyList<RestaurantEligibilityDto> Eligible,
        Guid SelectedRestaurantId);

    private sealed record FamilyReservationOutcome(
        Guid FamilyGroupId,
        Guid PaymentAnchorSubscriptionId,
        IReadOnlyList<Guid> SubscriptionIds,
        DateTime ExpiresAtUtc,
        decimal FinalPrice,
        IReadOnlyList<PendingPoolCapture> PoolCaptures,
        Guid BatchId, long BatchVersion, Guid AttemptId, bool OwnsInitialization, string? PaymentUrl = null,
        string? FailureCode = null);

    private sealed class FamilyCheckoutReservationException(string code) : Exception(code)
    {
        public string Code { get; } = code;
    }
}
