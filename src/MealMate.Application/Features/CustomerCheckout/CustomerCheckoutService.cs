using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Application.Features.Offers.Services;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MealMate.Application.Features.CustomerCheckout;

public sealed class CustomerCheckoutService : ICustomerCheckoutService
{
    private readonly IAppDbContext _db;
    private readonly IPaymentGatewayService _payments;
    private readonly IDateTimeProvider _clock;
    private readonly IUserAccountService _users;
    private readonly IRestaurantEligibilityService _eligibility;
    private readonly ICapacityConcurrencyLock _capacityLock;
    private readonly ICheckoutTransactionExecutor _transactionExecutor;
    private readonly ISubscriptionScheduleService _schedule;
    private readonly IRestaurantPoolSnapshotService _restaurantPool;
    private readonly IConfiguration? _configuration;
    private readonly IAllowedSubscriptionDurationService _allowedDurations;
    private readonly ISubscriptionPaymentProcessor _paymentProcessor;
    private readonly ISubscriptionCheckoutReadinessService _readiness;
    private readonly ISender _sender;
    private readonly IRestaurantCapacityReader _capacityReader;
    private readonly ISubscriptionFlowCouponService _coupons;
    private readonly ISandboxCheckoutService? _sandbox;

    public CustomerCheckoutService(
        IAppDbContext db,
        IPaymentGatewayService payments,
        IDateTimeProvider clock,
        IUserAccountService users,
        IRestaurantEligibilityService eligibility,
        ICapacityConcurrencyLock capacityLock,
        ICheckoutTransactionExecutor transactionExecutor,
        ISubscriptionScheduleService schedule,
        IRestaurantPoolSnapshotService restaurantPool,
        IConfiguration? configuration,
        IAllowedSubscriptionDurationService allowedDurations,
        ISubscriptionPaymentProcessor paymentProcessor,
        ISubscriptionCheckoutReadinessService readiness,
        ISender sender,
        IRestaurantCapacityReader capacityReader,
        ISandboxCheckoutService? sandbox = null,
        ISubscriptionFlowCouponService? couponService = null,
        IApplicationLockManager? applicationLocks = null)
    {
        _db = db;
        _payments = payments;
        _clock = clock;
        _users = users;
        _eligibility = eligibility;
        _capacityLock = capacityLock;
        _transactionExecutor = transactionExecutor;
        _schedule = schedule;
        _restaurantPool = restaurantPool;
        _configuration = configuration;
        _allowedDurations = allowedDurations;
        _paymentProcessor = paymentProcessor;
        _readiness = readiness;
        _sender = sender;
        _capacityReader = capacityReader;
        _sandbox = sandbox;
        _coupons = couponService ?? new SubscriptionFlowCouponService(db, new OfferEligibilityEngine(), clock, applicationLocks);
    }

    public async Task<Result<IReadOnlyList<CheckoutCatalogItem>>> GetCatalogAsync(CancellationToken ct = default)
    {
        var items = await (from p in _db.Programs.AsNoTracking()
                           join b in _db.Bundles.AsNoTracking() on p.Id equals b.ProgramId
                           where p.IsActive && p.Status == ProgramStatus.Active && b.IsActive && b.Status == ProgramStatus.Active
                           orderby p.SortOrder, b.SortOrder
                           select new CheckoutCatalogItem(p.Id, p.NameAr, p.NameEn, b.Id, b.NameAr, b.NameEn)).ToListAsync(ct);
        return Result.Success<IReadOnlyList<CheckoutCatalogItem>>(items);
    }

    public async Task<Result<IReadOnlyList<EligibleRestaurantDto>>> GetRestaurantsAsync(
        CheckoutAvailabilityRequest request, Guid? customerUserId, CancellationToken ct = default)
    {
        if (!IsValid(request, out var error)) return Error.Validation(error);
        var durationCheck = await _allowedDurations.EvaluateAsync(request.DurationDays, request.IncludeFriday, ct);
        if (!durationCheck.IsAllowed) return Error.Validation(durationCheck.ErrorCode ?? "subscription_flow.duration_not_available");

        var serviceDates = _schedule.GetServiceDates(request.StartDate, request.DurationDays, request.IncludeFriday);
        var fridayDays = _schedule.CountFridays(serviceDates);
        var pricing = await _sender.Send(new PreviewCustomerPricingQuery(request.ProgramId, request.BundleId, request.Tier, request.DurationDays,
            IncludeFriday: request.IncludeFriday, FridayDays: fridayDays, CustomerId: customerUserId), ct);
        if (pricing.IsFailure) return pricing.Error;

        var rows = await _eligibility.EvaluateAsync(new RestaurantEligibilityRequest(request.ProgramId, request.BundleId, request.Tier, request.RegionId, request.StartDate, request.DurationDays, request.IncludeFriday, customerUserId), ct);
        var list = rows.Where(x => x.IsEligible).Select(x => new EligibleRestaurantDto(x.RestaurantId, x.NameAr, x.NameEn,
            x.MenuPlanVersionsByDate[0].MenuPlanVersionId, pricing.Value.FinalCustomerPrice, request.IncludeFriday, fridayDays, serviceDates.Count)).ToList();

        return Result.Success<IReadOnlyList<EligibleRestaurantDto>>(list);
    }

    public async Task<Result<CheckoutStartedDto>> StartCheckoutAsync(
        StartCheckoutRequest request, Guid customerUserId, CancellationToken ct = default)
    {
        if (!IsAllowedRedirectUrl(request.CallbackUrl) || !IsAllowedRedirectUrl(request.ErrorUrl))
            return Error.Validation("checkout.redirect_url_not_allowed");

        var user = await _users.FindByIdAsync(customerUserId, ct);
        if (user is null) return Error.Validation("checkout.customer_not_found");

        var gate = await _readiness.EvaluateAsync(customerUserId, request.ProgramId, request.BundleId, request.Tier, request.DurationDays, request.IncludeFriday, ct, request.StartDate);
        if (gate.IsFailure) return gate.Error;

        var profileBlock = SubscriptionCheckoutGateCodes.ProfileCheckoutBlockCode(gate.Value);
        if (profileBlock is not null)
            return Error.Conflict(profileBlock);

        if (gate.Value.CheckoutBlockReason == SubscriptionCheckoutGateCodes.ExistingSubscription)
        {
            return new Error("checkout.existing_subscription", null, ErrorType.Conflict,
                [new { error = "checkout.existing_subscription", subscriptionId = gate.Value.BlockingSubscriptionId, status = gate.Value.BlockingSubscriptionStatus }]);
        }

        if (gate.Value.Address is null)
            return Error.Conflict("checkout.address_required");

        var regionId = gate.Value.Address.RegionId;
        if (!_schedule.TryResolveCheckoutStart(request.StartDate, request.IncludeFriday, out var startDate, out var startError))
            return Error.Validation(startError ?? "checkout.start_date_required");

        var chosenStartDate = request.StartDate;
        var deliveryAddress = gate.Value.Address.AddressLine;
        var availability = new CheckoutAvailabilityRequest(request.ProgramId, request.BundleId, request.Tier, request.DurationDays, startDate, regionId, request.IncludeFriday);
        if (!IsValid(availability, out var error)) return Error.Validation(error);

        var durationCheck = await _allowedDurations.EvaluateAsync(request.DurationDays, request.IncludeFriday, ct);
        if (!durationCheck.IsAllowed) return Error.Validation(durationCheck.ErrorCode ?? "subscription_flow.duration_not_available");

        var serviceDates = _schedule.GetServiceDates(startDate, request.DurationDays, request.IncludeFriday);
        var fridayDays = _schedule.CountFridays(serviceDates);
        var preview = await _sender.Send(new PreviewCustomerPricingQuery(request.ProgramId, request.BundleId, request.Tier, request.DurationDays,
            IncludeFriday: request.IncludeFriday, FridayDays: fridayDays, CustomerId: customerUserId,
            PromoCode: request.PromoCode, CustomerSubscriptionType: "Individual"), ct);
        if (preview.IsFailure) return preview.Error;

        // Coupon gate: a rejected code aborts checkout before any subscription, capacity hold or
        // payment session exists. The final price is always recomputed here, never taken from the client.
        SubscriptionCouponEvaluation? couponEvaluation = null;
        var pricingResult = preview.Value;
        var promoCode = request.PromoCode?.Trim();
        if (!string.IsNullOrWhiteSpace(promoCode))
        {
            couponEvaluation = await _coupons.EvaluateAsync(
                promoCode, customerUserId, request.ProgramId, request.BundleId, request.DurationDays,
                preview.Value.FinalCustomerPrice, ct, subscriptionType: "Individual", tier: request.Tier);
            if (!couponEvaluation.IsValid)
            {
                return new Error("checkout.coupon_rejected", couponEvaluation.MessageEn, ErrorType.Validation,
                [
                    new
                    {
                        error = "checkout.coupon_rejected",
                        coupon = new SubscriptionSummaryCouponDto(
                            promoCode, couponEvaluation.Status, couponEvaluation.OfferId, 0m,
                            couponEvaluation.MessageAr, couponEvaluation.MessageEn)
                    }
                ]);
            }

            var discounted = await _sender.Send(new PreviewCustomerPricingQuery(request.ProgramId, request.BundleId, request.Tier, request.DurationDays,
                IncludeFriday: request.IncludeFriday, FridayDays: fridayDays,
                CustomerId: customerUserId, PromoCode: promoCode, CustomerSubscriptionType: "Individual"), ct);
            if (discounted.IsFailure) return discounted.Error;
            pricingResult = discounted.Value;
            if (pricingResult.FinalCustomerPrice <= 0m)
            {
                return Error.Validation("checkout.coupon_full_discount_unsupported",
                    "The coupon reduces the price to zero; no settlement path exists for a zero-amount subscription.");
            }
        }

        CheckoutReservationOutcome reservation;
        try
        {
            reservation = await _transactionExecutor.ExecuteAsync(async transactionCt =>
            {
                var checkedAt = _clock.UtcNow;
                var allRows = await _eligibility.EvaluateAsync(new RestaurantEligibilityRequest(request.ProgramId, request.BundleId, request.Tier, regionId, startDate, request.DurationDays, request.IncludeFriday, customerUserId), transactionCt);
                var eligible = allRows.Where(x => x.IsEligible).OrderBy(x => x.NameEn).ThenBy(x => x.RestaurantId).ToList();
                var restaurant = eligible.FirstOrDefault();
                if (restaurant is null) throw new CheckoutReservationException("restaurant.not_eligible");
                var resolvedRegionId = regionId;
                if (resolvedRegionId == Guid.Empty)
                {
                    resolvedRegionId = await _db.RestaurantServiceRegions.AsNoTracking()
                        .Where(x => x.RestaurantId == restaurant.RestaurantId && x.IsActive)
                        .Select(x => x.RegionId)
                        .FirstOrDefaultAsync(transactionCt);
                    if (resolvedRegionId == Guid.Empty) throw new CheckoutReservationException("restaurant.not_eligible");
                }
                // Lock previous pending checkouts before the capacity lock so this path
                // uses the same subscription->capacity order as the payment flow (G45).
                var prevSubIds = await _db.Subscriptions.AsNoTracking()
                    .Where(s => s.CustomerUserId == customerUserId && s.FamilyGroupId == null && (s.Status == SubscriptionStatus.PendingPayment || s.Status == SubscriptionStatus.Draft))
                    .Select(s => s.Id)
                    .ToListAsync(transactionCt);
                foreach (var prevSubId in prevSubIds.OrderBy(x => x))
                    await _capacityLock.AcquireSubscriptionAsync(prevSubId, transactionCt);

                await _capacityLock.AcquireAsync(restaurant.RestaurantId, serviceDates, transactionCt);

                // The eligibility snapshot above predates serialization; re-check capacity
                // through the unified reader now that this transaction holds the lock (G08).
                var capacityUnits = await _capacityReader.ReadAsync(
                    serviceDates.Distinct().Select(date => (restaurant.RestaurantId, date)).ToList(), transactionCt);
                foreach (var date in serviceDates.Distinct())
                {
                    var units = capacityUnits[(restaurant.RestaurantId, date)];
                    if (units.LimitBoxes is not { } limit || limit <= 0 || units.TotalCommittedUnits + 1 > limit)
                        throw new CheckoutReservationException("checkout.capacity_unavailable");
                }

                var create = await _sender.Send(new CreateSubscriptionCommand(customerUserId, request.ProgramId, request.BundleId, request.Tier, request.DurationDays,
                    startDate, deliveryAddress, RegionId: resolvedRegionId, CorrelationId: request.CorrelationId, IncludeFriday: request.IncludeFriday,
                    PromoCodeId: couponEvaluation is { IsValid: true }
                        && (couponEvaluation.PromoCodeId.HasValue ? pricingResult.PromoCodeDiscountApplied > 0m : pricingResult.CampaignDiscountApplied > 0m)
                        ? couponEvaluation.PromoCodeId : null,
                    CouponOfferId: couponEvaluation is { IsValid: true }
                        && !couponEvaluation.PromoCodeId.HasValue && pricingResult.CampaignDiscountApplied > 0m
                        ? couponEvaluation.OfferId : null,
                    CouponDiscountKwd: couponEvaluation is { IsValid: true }
                        ? couponEvaluation.PromoCodeId.HasValue ? pricingResult.PromoCodeDiscountApplied : pricingResult.CampaignDiscountApplied
                        : 0m,
                    CheckoutAnchorDate: chosenStartDate), transactionCt);
                if (create.IsFailure) throw new CheckoutReservationException(create.Error.Code);
                var expiresAt = _clock.UtcNow.AddMinutes(15);

                // The coupon hold lives in the same transaction as the subscription so concurrent
                // checkouts cannot exceed the offer's usage limits; released on failure/expiry.
                if (couponEvaluation is { IsValid: true }
                    && (couponEvaluation.PromoCodeId.HasValue
                        ? pricingResult.PromoCodeDiscountApplied > 0m
                        : pricingResult.CampaignDiscountApplied > 0m))
                    await _coupons.CreateCheckoutHoldAsync(
                        couponEvaluation, create.Value.Id, customerUserId,
                        couponEvaluation.PromoCodeId.HasValue ? pricingResult.PromoCodeDiscountApplied : pricingResult.CampaignDiscountApplied,
                        transactionCt);

                foreach (var resolved in restaurant.MenuPlanVersionsByDate)
                    _db.CapacityHolds.Add(new CapacityHold { RestaurantId = restaurant.RestaurantId, SubscriptionId = create.Value.Id, MenuPlanVersionId = resolved.MenuPlanVersionId, ServiceDate = resolved.ServiceDate, Quantity = 1, Status = CapacityHoldStatus.PendingPayment, ExpiresAtUtc = expiresAt });
                if (prevSubIds.Count > 0)
                {
                    var previousPendingHolds = await _db.CapacityHolds
                        .Where(x => prevSubIds.Contains(x.SubscriptionId) && x.Status == CapacityHoldStatus.PendingPayment)
                        .ToListAsync(transactionCt);
                    foreach (var h in previousPendingHolds)
                    {
                        h.Status = CapacityHoldStatus.Released;
                        h.ReleasedAtUtc = _clock.UtcNow;
                    }
                    foreach (var prevSubId in prevSubIds)
                    {
                        await _coupons.ReleaseCheckoutRedemptionAsync(prevSubId, transactionCt);
                    }
                }

                await _db.SaveChangesAsync(transactionCt);
                var pool = await _restaurantPool.CaptureAsync(create.Value.Id, eligible, restaurant.RestaurantId, transactionCt);
                if (!pool.IsSuccess) throw new CheckoutReservationException(pool.ErrorCode ?? "checkout.restaurant_pool_failed");
                return new CheckoutReservationOutcome(create.Value, restaurant, null, checkedAt, expiresAt, pool.PoolSnapshotId, pool.RestaurantCount, resolvedRegionId);
            }, ct);
        }
        catch (CheckoutReservationException ex)
        {
            return new Error(ex.Code, "Checkout could not be completed. Review your selections and try again.", ErrorType.Validation,
                [new { error = ex.Code, message = "Checkout could not be completed. Review your selections and try again." }]);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new Error("checkout.coupon_limit_reached", "The coupon is no longer available or usage limit was reached.", ErrorType.Conflict,
                [new { error = "checkout.coupon_limit_reached", message = "The coupon is no longer available or usage limit was reached." }]);
        }

        if (reservation.ErrorCode is not null)
            return new Error(reservation.ErrorCode, "Checkout could not be completed. Review your selections and try again.", ErrorType.Validation,
                [new { error = reservation.ErrorCode, message = "Checkout could not be completed. Review your selections and try again." }]);

        var createSub = reservation.Subscription!;
        var selectedRestaurant = reservation.Restaurant!;
        var subscriptionId = createSub.Id;
        var expiresAtUtc = reservation.ExpiresAtUtc;
        var checkedAtUtc = reservation.CheckedAtUtc;

        string paymentUrl;
        PaymentCreationReceipt? paymentReceipt = null;
        try
        {
            paymentUrl = _sandbox is not null && await _sandbox.IsEnabledAsync(ct)
                ? await _sandbox.StartAsync(subscriptionId, customerUserId, expiresAtUtc, request.CallbackUrl, ct)
                : request.PaymentMethodId == SandboxCheckoutPolicy.PaymentMethodId
                    ? throw new InvalidOperationException("Sandbox completion is disabled.")
                    : (paymentReceipt = await _payments.ExecutePaymentAsync(createSub.PaidAmount, user.FullName ?? "", user.Email ?? "", user.PhoneNumber ?? "",
                        request.CallbackUrl, request.ErrorUrl, subscriptionId.ToString(), request.PaymentMethodId, ct)).PaymentUrl;
        }
        catch (Exception ex)
        {
            var failedSubscription = await _db.Subscriptions.FirstAsync(x => x.Id == subscriptionId, ct);
            failedSubscription.Status = SubscriptionStatus.PaymentFailed;
            failedSubscription.LastOperationAtUtc = _clock.UtcNow;
            var failedHolds = await _db.CapacityHolds.Where(x => x.SubscriptionId == subscriptionId && x.Status == CapacityHoldStatus.PendingPayment).ToListAsync(ct);
            foreach (var hold in failedHolds) { hold.Status = CapacityHoldStatus.Released; hold.ReleasedAtUtc = _clock.UtcNow; }
            await _coupons.ReleaseCheckoutRedemptionAsync(subscriptionId, ct);
            _db.SubscriptionOperations.Add(new SubscriptionOperation
            {
                SubscriptionId = subscriptionId, OperationType = "PAYMENT_INITIALIZATION_FAILED",
                PreviousStatus = SubscriptionStatus.PendingPayment, NewStatus = SubscriptionStatus.PaymentFailed,
                Reason = ex.Message,
                PerformedByUserId = customerUserId, PerformedByUserType = UserType.Customer,
                CorrelationId = request.CorrelationId, TimestampUtc = _clock.UtcNow
            });
            await _db.SaveChangesAsync(ct);
            return new Error("checkout.payment_init_failed", "checkout.payment_init_failed", ErrorType.Failure);
        }

        var gatewayPaymentId = paymentReceipt?.PaymentHandle;
        if (!string.IsNullOrWhiteSpace(gatewayPaymentId))
        {
            var pending = await _db.Subscriptions.FirstAsync(x => x.Id == subscriptionId, ct);
            pending.PaymentId = gatewayPaymentId;
            await _db.SaveChangesAsync(ct);
        }

        var priceSnapshotId = await _db.SubscriptionSnapshots.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId).OrderBy(x => x.CapturedAtUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        return new CheckoutStartedDto(subscriptionId, paymentUrl, expiresAtUtc, selectedRestaurant.MenuPlanVersionsByDate[0].MenuPlanVersionId,
            createSub.PaidAmount, priceSnapshotId, checkedAtUtc, selectedRestaurant.MenuPlanVersionsByDate, reservation.PoolSnapshotId,
            reservation.RestaurantCount, "PendingPayment", "PendingPayment", request.IncludeFriday, fridayDays, serviceDates.Count,
            selectedRestaurant.RestaurantId, reservation.RegionId, startDate, deliveryAddress, chosenStartDate);
    }

    public async Task<Result<SubscriptionPaymentStatusDto>> GetPaymentStatusAsync(Guid id, Guid customerUserId, CancellationToken ct = default)
    {
        if (!await OwnsCheckoutTargetAsync(id, customerUserId, ct)) return Error.NotFound("subscription.not_found");
        var subscription = await ResolveCheckoutSubscriptionAsync(id, customerUserId, ct);
        if (subscription is null) return Error.NotFound("subscription.not_found");
        if (subscription.Status == SubscriptionStatus.PendingPayment)
            subscription = await SyncPendingPaymentAsync(subscription, ct);

        var familyGroup = await FindOwnedFamilyGroupAsync(id, subscription.FamilyGroupId, customerUserId, ct);
        var isPaid = subscription.Status is SubscriptionStatus.Paid or SubscriptionStatus.Scheduled or SubscriptionStatus.Active
            || familyGroup?.Status == FamilyGroupStatus.Active;
        if (!isPaid && familyGroup is not null)
        {
            isPaid = await _db.Subscriptions.AsNoTracking().AnyAsync(
                x => x.FamilyGroupId == familyGroup.Id
                     && (x.Status == SubscriptionStatus.Paid
                         || x.Status == SubscriptionStatus.Scheduled
                         || x.Status == SubscriptionStatus.Active),
                ct);
        }

        var isFailed = (subscription.Status is SubscriptionStatus.PaymentFailed or SubscriptionStatus.Cancelled)
            && familyGroup?.Status != FamilyGroupStatus.Active;
        var forFamilyGroup = familyGroup is not null && familyGroup.Id == id;
        var amount = forFamilyGroup ? familyGroup!.QuotedFinalPrice : subscription.PaidAmount.Amount;
        var currency = forFamilyGroup && !string.IsNullOrWhiteSpace(familyGroup!.CurrencyCode)
            ? familyGroup.CurrencyCode
            : subscription.PaidAmount.CurrencyCode;
        var status = forFamilyGroup
            ? (isPaid ? nameof(SubscriptionStatus.Paid) : familyGroup!.Status.ToString())
            : subscription.Status.ToString();
        return new SubscriptionPaymentStatusDto(id, status, isPaid, isFailed, amount, currency);
    }

    public async Task<Result<CustomerSubscriptionResumePreviewDto>> GetResumePreviewAsync(Guid id, Guid customerUserId, CancellationToken ct = default)
    {
        if (!await OwnsSubscriptionAsync(id, customerUserId, ct)) return Error.NotFound("subscription.not_found");

        var subscription = await _db.Subscriptions.AsNoTracking()
            .Include(x => x.Program)
            .Include(x => x.Bundle)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (subscription is null) return Error.NotFound("subscription.not_found");
        if (subscription.FamilyGroupId is not null)
            return Error.Conflict("family.actual_checkout_required");

        if (subscription.Status is not (SubscriptionStatus.Draft or SubscriptionStatus.PendingPayment or SubscriptionStatus.PaymentFailed or SubscriptionStatus.Expired))
            return Error.Conflict("subscription.cannot_resume_completed_or_active");

        var holds = await _db.CapacityHolds.AsNoTracking()
            .Where(x => x.SubscriptionId == id && x.Status == CapacityHoldStatus.PendingPayment)
            .ToListAsync(ct);

        var activeHolds = holds.Where(x => x.ExpiresAtUtc > _clock.UtcNow).ToList();
        var isHoldActive = activeHolds.Count > 0;
        var holdExpiresAtUtc = isHoldActive ? activeHolds.Max(x => (DateTime?)x.ExpiresAtUtc) : null;
        var holdRemainingSeconds = isHoldActive ? Math.Max(0, (int)(holdExpiresAtUtc!.Value - _clock.UtcNow).TotalSeconds) : 0;

        var isDateValid = subscription.StartDate >= _schedule.GetKuwaitToday();
        var canPayDirectly = isHoldActive && isDateValid;
        var reason = canPayDirectly ? null : (!isDateValid ? "start_date_passed" : "capacity_hold_expired");

        _schedule.TryResolveCheckoutStart(_clock.TodayUtc.AddDays(1), subscription.IncludeFriday, out var suggestedDate, out _);

        var pricingRow = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, ct);
        var tierLabels = SubscriptionPricingSettingsReader.Deserialize(pricingRow?.ValueJson).TierLabels;
        var (tierNameAr, tierNameEn, tierLogoKey) = subscription.SelectedTier switch
        {
            RestaurantTier.Platinum => (tierLabels.PlatinumAr, tierLabels.PlatinumEn, tierLabels.PlatinumLogoStorageKey),
            RestaurantTier.Elite => (tierLabels.EliteAr, tierLabels.EliteEn, tierLabels.EliteLogoStorageKey),
            _ => (tierLabels.BasicAr, tierLabels.BasicEn, tierLabels.BasicLogoStorageKey)
        };
        var tierLogoUrl = CatalogImageStorage.ToContentUrl(tierLogoKey);

        return new CustomerSubscriptionResumePreviewDto(
            subscription.Id,
            subscription.Status.ToString(),
            SubscriptionLocalization.StatusNameAr(subscription.Status),
            subscription.ProgramId,
            subscription.Program?.NameAr ?? string.Empty,
            subscription.Program?.NameEn ?? string.Empty,
            subscription.BundleId,
            subscription.Bundle?.NameAr ?? string.Empty,
            subscription.Bundle?.NameEn ?? string.Empty,
            subscription.SelectedTier,
            tierNameAr,
            tierNameEn,
            tierLogoUrl,
            subscription.Bundle?.MealsPerDay ?? 0,
            subscription.DurationDays,
            subscription.IncludeFriday,
            subscription.StartDate,
            subscription.EndDate,
            subscription.PaidAmount.Amount,
            subscription.PaidAmount.CurrencyCode,
            subscription.DeliveryAddress,
            canPayDirectly,
            reason,
            canPayDirectly ? holdRemainingSeconds : null,
            canPayDirectly ? holdExpiresAtUtc : null,
            suggestedDate,
            $"/api/v1/customer/subscriptions/{subscription.Id}/resume",
            "checkout",
            $"/api/v1/customer/subscriptions/{subscription.Id}/resume",
            canPayDirectly ? subscription.StartDate : (suggestedDate == default ? subscription.StartDate : suggestedDate));
    }

    public async Task<Result<CheckoutStartedDto>> ResumeCheckoutAsync(
        Guid id, ResumeCheckoutRequest? request, Guid customerUserId, CancellationToken ct = default)
    {
        if (!await OwnsSubscriptionAsync(id, customerUserId, ct)) return Error.NotFound("subscription.not_found");

        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (subscription is null) return Error.NotFound("subscription.not_found");
        if (subscription.FamilyGroupId is not null)
            return Error.Conflict("family.actual_checkout_required");

        if (subscription.Status is not (SubscriptionStatus.Draft or SubscriptionStatus.PendingPayment or SubscriptionStatus.PaymentFailed or SubscriptionStatus.Expired))
            return Error.Conflict("subscription.cannot_resume_completed_or_active");

        var user = await _users.FindByIdAsync(customerUserId, ct);
        if (user is null) return Error.Validation("checkout.customer_not_found");

        var callbackUrl = request?.CallbackUrl ?? $"{_configuration?["Checkout:DefaultCallbackUrl"] ?? "https://maelmate.runasp.net/checkout/callback"}";
        var errorUrl = request?.ErrorUrl ?? $"{_configuration?["Checkout:DefaultErrorUrl"] ?? "https://maelmate.runasp.net/checkout/error"}";

        if (!IsAllowedRedirectUrl(callbackUrl) || !IsAllowedRedirectUrl(errorUrl))
            return Error.Validation("checkout.redirect_url_not_allowed");

        // Resume reuses the stored first-delivery StartDate: the +3/+4 lead was applied once at
        // checkout and must never be applied again (Oct 11 must not become Oct 15). Only a
        // genuinely new chosen day sent by the client re-derives a fresh first delivery.
        var requestedStartDate = request?.StartDate;
        DateOnly startDate;
        DateOnly offerAnchor;
        if (requestedStartDate.HasValue && requestedStartDate.Value != subscription.StartDate)
        {
            if (!_schedule.TryResolveCheckoutStart(requestedStartDate, subscription.IncludeFriday, out startDate, out var startError))
                return Error.Validation(startError ?? "checkout.start_date_invalid");
            offerAnchor = requestedStartDate.Value;
        }
        else
        {
            if (subscription.StartDate < _schedule.GetKuwaitToday())
                return Error.Validation("checkout.start_date_invalid");
            startDate = subscription.StartDate;
            offerAnchor = subscription.CheckoutAnchorDate ?? subscription.StartDate;
        }

        var durationCheck = await _allowedDurations.EvaluateAsync(subscription.DurationDays, subscription.IncludeFriday, ct);
        if (!durationCheck.IsAllowed) return Error.Validation(durationCheck.ErrorCode ?? "subscription_flow.duration_not_available");

        var serviceDates = _schedule.GetServiceDates(startDate, subscription.DurationDays, subscription.IncludeFriday);
        var fridayDays = _schedule.CountFridays(serviceDates);

        var preview = await _sender.Send(new PreviewCustomerPricingQuery(subscription.ProgramId, subscription.BundleId, subscription.SelectedTier, subscription.DurationDays,
            IncludeFriday: subscription.IncludeFriday, FridayDays: fridayDays, CustomerId: customerUserId), ct);
        if (preview.IsFailure) return preview.Error;

        // Resume never creates a new discount: the subscription's existing coupon hold is
        // re-validated against current policy and the price is re-derived from it. If the
        // coupon is no longer valid the customer sees a clear rejection before any payment.
        var pricingResult = preview.Value;
        var heldRedemption = await _db.OfferRedemptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SubscriptionId == subscription.Id, ct);
        var heldPromoRedemption = await _db.PromoCodeRedemptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SubscriptionId == subscription.Id
                && x.Status == PromoCodeRedemptionStatus.Held, ct);
        if (heldRedemption is not null || heldPromoRedemption is not null)
        {
            var heldOffer = heldRedemption is null ? null : await _db.Offers.AsNoTracking()
                .Include(x => x.PackageTargets)
                .FirstOrDefaultAsync(x => x.Id == heldRedemption.OfferId, ct);
            var heldPromo = heldPromoRedemption is null ? null : await _db.PromoCodes.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == heldPromoRedemption.PromoCodeId, ct);
            var heldCode = heldPromo?.Code ?? heldOffer?.PromoCode;
            var reevaluation = await _coupons.EvaluateAsync(
                heldCode, customerUserId, subscription.ProgramId, subscription.BundleId, subscription.DurationDays,
                preview.Value.FinalCustomerPrice, ct, excludeSubscriptionId: subscription.Id,
                tier: subscription.SelectedTier);
            if (!reevaluation.IsValid)
            {
                return new Error("checkout.coupon_no_longer_valid", reevaluation.MessageEn, ErrorType.Validation,
                [
                    new
                    {
                        error = "checkout.coupon_no_longer_valid",
                        coupon = new SubscriptionSummaryCouponDto(
                            heldCode ?? string.Empty, reevaluation.Status, reevaluation.OfferId, 0m,
                            reevaluation.MessageAr, reevaluation.MessageEn)
                    }
                ]);
            }

            var discounted = await _sender.Send(new PreviewCustomerPricingQuery(subscription.ProgramId, subscription.BundleId, subscription.SelectedTier, subscription.DurationDays,
                IncludeFriday: subscription.IncludeFriday, FridayDays: fridayDays,
                CustomerId: customerUserId, PromoCode: heldCode, CustomerSubscriptionType: "Individual",
                ExcludeSubscriptionId: subscription.Id), ct);
            if (discounted.IsFailure) return discounted.Error;
            pricingResult = discounted.Value;
            if (pricingResult.FinalCustomerPrice <= 0m)
            {
                return Error.Validation("checkout.coupon_full_discount_unsupported",
                    "The coupon reduces the price to zero; no settlement path exists for a zero-amount subscription.");
            }
        }

        var regionId = subscription.RegionId ?? Guid.Empty;

        CheckoutReservationOutcome reservation;
        try
        {
            reservation = await _transactionExecutor.ExecuteAsync(async transactionCt =>
            {
                await _capacityLock.AcquireSubscriptionAsync(subscription.Id, transactionCt);
                var currentStatus = await _db.Subscriptions.AsNoTracking().Where(x => x.Id == subscription.Id)
                    .Select(x => x.Status).FirstAsync(transactionCt);
                if (currentStatus is not (SubscriptionStatus.Draft or SubscriptionStatus.PendingPayment or SubscriptionStatus.PaymentFailed or SubscriptionStatus.Expired))
                    throw new CheckoutReservationException("subscription.cannot_resume_completed_or_active");
                var checkedAt = _clock.UtcNow;
                var allRows = await _eligibility.EvaluateAsync(new RestaurantEligibilityRequest(subscription.ProgramId, subscription.BundleId, subscription.SelectedTier, regionId, startDate, subscription.DurationDays, subscription.IncludeFriday, customerUserId), transactionCt);
                var eligible = allRows.Where(x => x.IsEligible).OrderBy(x => x.NameEn).ThenBy(x => x.RestaurantId).ToList();
                var restaurant = eligible.FirstOrDefault();
                if (restaurant is null) throw new CheckoutReservationException("restaurant.not_eligible");

                var resolvedRegionId = regionId;
                if (resolvedRegionId == Guid.Empty)
                {
                    resolvedRegionId = await _db.RestaurantServiceRegions.AsNoTracking()
                        .Where(x => x.RestaurantId == restaurant.RestaurantId && x.IsActive)
                        .Select(x => x.RegionId)
                        .FirstOrDefaultAsync(transactionCt);
                    if (resolvedRegionId == Guid.Empty) throw new CheckoutReservationException("restaurant.not_eligible");
                }

                await _capacityLock.AcquireAsync(restaurant.RestaurantId, serviceDates, transactionCt);

                var oldHolds = await _db.CapacityHolds.IgnoreQueryFilters()
                    .Where(x => x.SubscriptionId == subscription.Id)
                    .ToListAsync(transactionCt);
                if (oldHolds.Count > 0)
                {
                    _db.CapacityHolds.RemoveRange(oldHolds);
                    await _db.SaveChangesAsync(transactionCt);
                }

                var expiresAt = _clock.UtcNow.AddMinutes(15);
                foreach (var resolved in restaurant.MenuPlanVersionsByDate)
                    _db.CapacityHolds.Add(new CapacityHold { RestaurantId = restaurant.RestaurantId, SubscriptionId = subscription.Id, MenuPlanVersionId = resolved.MenuPlanVersionId, ServiceDate = resolved.ServiceDate, Quantity = 1, Status = CapacityHoldStatus.PendingPayment, ExpiresAtUtc = expiresAt });

                subscription.StartDate = startDate;
                if (requestedStartDate.HasValue && requestedStartDate.Value != startDate)
                    subscription.CheckoutAnchorDate = offerAnchor;
                subscription.EndDate = serviceDates.Last();
                subscription.Status = SubscriptionStatus.PendingPayment;
                subscription.LastOperationAtUtc = _clock.UtcNow;
                subscription.PaidAmount = new Money(pricingResult.FinalCustomerPrice, "KWD");
                if (resolvedRegionId != Guid.Empty)
                    subscription.RegionId = resolvedRegionId;

                await _db.SaveChangesAsync(transactionCt);

                var oldPoolDays = await _db.SubscriptionRestaurantDaySnapshots.IgnoreQueryFilters()
                    .Where(x => x.SubscriptionId == subscription.Id)
                    .ToListAsync(transactionCt);
                if (oldPoolDays.Count > 0)
                {
                    _db.SubscriptionRestaurantDaySnapshots.RemoveRange(oldPoolDays);
                    await _db.SaveChangesAsync(transactionCt);
                }

                var oldPoolRests = await _db.SubscriptionRestaurantSnapshots.IgnoreQueryFilters()
                    .Where(x => x.SubscriptionId == subscription.Id)
                    .ToListAsync(transactionCt);
                if (oldPoolRests.Count > 0)
                {
                    _db.SubscriptionRestaurantSnapshots.RemoveRange(oldPoolRests);
                    await _db.SaveChangesAsync(transactionCt);
                }

                var oldPools = await _db.SubscriptionRestaurantPoolSnapshots.IgnoreQueryFilters()
                    .Where(x => x.SubscriptionId == subscription.Id)
                    .ToListAsync(transactionCt);
                if (oldPools.Count > 0)
                {
                    _db.SubscriptionRestaurantPoolSnapshots.RemoveRange(oldPools);
                    await _db.SaveChangesAsync(transactionCt);
                }

                var pool = await _restaurantPool.CaptureAsync(subscription.Id, eligible, restaurant.RestaurantId, transactionCt);
                if (!pool.IsSuccess) throw new CheckoutReservationException(pool.ErrorCode ?? "checkout.restaurant_pool_failed");

                return new CheckoutReservationOutcome(null, restaurant, null, checkedAt, expiresAt, pool.PoolSnapshotId, pool.RestaurantCount, resolvedRegionId);
            }, ct);
        }
        catch (CheckoutReservationException ex)
        {
            return new Error(ex.Code, "Checkout could not be completed. Review your selections and try again.", ErrorType.Validation,
                [new { error = ex.Code, message = "Checkout could not be completed. Review your selections and try again." }]);
        }
        catch (DbUpdateException)
        {
            return Error.Conflict("subscription.resume_state_conflict");
        }

        if (reservation.ErrorCode is not null)
            return new Error(reservation.ErrorCode, "Checkout could not be completed. Review your selections and try again.", ErrorType.Validation,
                [new { error = reservation.ErrorCode, message = "Checkout could not be completed. Review your selections and try again." }]);

        var restaurantResult = reservation.Restaurant!;
        var expiresAtUtc = reservation.ExpiresAtUtc;
        var checkedAtUtc = reservation.CheckedAtUtc;

        string paymentUrl;
        PaymentCreationReceipt? paymentReceipt = null;
        try
        {
            paymentUrl = _sandbox is not null && await _sandbox.IsEnabledAsync(ct)
                ? await _sandbox.StartAsync(subscription.Id, customerUserId, expiresAtUtc, callbackUrl, ct)
                : request?.PaymentMethodId == SandboxCheckoutPolicy.PaymentMethodId
                    ? throw new InvalidOperationException("Sandbox completion is disabled.")
                    : (paymentReceipt = await _payments.ExecutePaymentAsync(subscription.PaidAmount.Amount, user.FullName ?? "", user.Email ?? "", user.PhoneNumber ?? "",
                        callbackUrl, errorUrl, subscription.Id.ToString(), request?.PaymentMethodId ?? 3, ct)).PaymentUrl;
        }
        catch (Exception ex)
        {
            subscription.Status = SubscriptionStatus.PaymentFailed;
            subscription.LastOperationAtUtc = _clock.UtcNow;
            var failedHolds = await _db.CapacityHolds.Where(x => x.SubscriptionId == subscription.Id && x.Status == CapacityHoldStatus.PendingPayment).ToListAsync(ct);
            foreach (var hold in failedHolds) { hold.Status = CapacityHoldStatus.Released; hold.ReleasedAtUtc = _clock.UtcNow; }
            _db.SubscriptionOperations.Add(new SubscriptionOperation
            {
                SubscriptionId = subscription.Id, OperationType = "PAYMENT_INITIALIZATION_FAILED",
                PreviousStatus = SubscriptionStatus.PendingPayment, NewStatus = SubscriptionStatus.PaymentFailed,
                Reason = ex.Message,
                PerformedByUserId = customerUserId, PerformedByUserType = UserType.Customer,
                TimestampUtc = _clock.UtcNow
            });
            await _db.SaveChangesAsync(ct);
            return new Error("checkout.payment_init_failed", "checkout.payment_init_failed", ErrorType.Failure);
        }

        var gatewayPaymentId = paymentReceipt?.PaymentHandle;
        if (!string.IsNullOrWhiteSpace(gatewayPaymentId))
        {
            subscription.PaymentId = gatewayPaymentId;
            await _db.SaveChangesAsync(ct);
        }

        var priceSnapshotId = await _db.SubscriptionSnapshots.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id).OrderBy(x => x.CapturedAtUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        var firstMenuPlanVersionId = restaurantResult.MenuPlanVersionsByDate.Count > 0
            ? restaurantResult.MenuPlanVersionsByDate[0].MenuPlanVersionId
            : Guid.Empty;

        return new CheckoutStartedDto(subscription.Id, paymentUrl, expiresAtUtc, firstMenuPlanVersionId,
            subscription.PaidAmount.Amount, priceSnapshotId, checkedAtUtc, restaurantResult.MenuPlanVersionsByDate, reservation.PoolSnapshotId,
            reservation.RestaurantCount, "PendingPayment", "PendingPayment", subscription.IncludeFriday, fridayDays, serviceDates.Count,
            restaurantResult.RestaurantId, reservation.RegionId, startDate, subscription.DeliveryAddress, offerAnchor);
    }

    public async Task<Result<SubscriptionRestaurantPoolDto>> GetSubscriptionRestaurantsAsync(
        Guid id, Guid customerUserId, string? query, int page, int pageSize, string? acceptLanguage = null, CancellationToken ct = default)
    {
        if (!await OwnsSubscriptionAsync(id, customerUserId, ct)) return Error.NotFound("subscription.not_found");
        var pool = await _db.SubscriptionRestaurantPoolSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriptionId == id, ct);
        if (pool is null) return Error.Conflict("subscription.restaurant_pool_not_ready");

        var queryable = _db.SubscriptionRestaurantSnapshots.AsNoTracking()
            .Where(x => x.SubscriptionId == id);

        var list = await queryable
            .OrderByDescending(x => x.IsInitiallySelected).ThenBy(x => x.NameEn)
            .ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            list = list.Where(x =>
                (x.NameAr != null && x.NameAr.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (x.NameEn != null && x.NameEn.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        var totalCount = list.Count;
        var pageReq = new PageRequest { Page = page, PageSize = pageSize };
        var totalPages = pageReq.NormalizedPageSize == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageReq.NormalizedPageSize);
        var pagedList = list
            .Skip(pageReq.Skip)
            .Take(pageReq.NormalizedPageSize)
            .ToList();

        var restaurantIds = pagedList.Select(x => x.RestaurantId).Distinct().ToList();
        var logos = await LoadRestaurantLogoUrlsAsync(restaurantIds, ct);
        var ratings = await _db.RestaurantRatings.AsNoTracking()
            .Where(x => restaurantIds.Contains(x.RestaurantId))
            .GroupBy(x => x.RestaurantId)
            .Select(g => new
            {
                RestaurantId = g.Key,
                Avg = g.Average(r => (decimal)r.Score),
                Count = g.Count()
            })
            .ToDictionaryAsync(x => x.RestaurantId, x => (Avg: Math.Round(x.Avg, 1), x.Count), ct);

        var pricingRow = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, ct);
        var tierLabels = SubscriptionPricingSettingsReader.Deserialize(pricingRow?.ValueJson).TierLabels;

        var culture = acceptLanguage?.ToLowerInvariant() ?? "ar";
        var isArabic = !culture.StartsWith("en", StringComparison.OrdinalIgnoreCase);

        var items = pagedList.Select(x =>
        {
            var ratingInfo = ratings.GetValueOrDefault(x.RestaurantId);
            var (tierNameAr, tierNameEn, logoKey) = x.Tier switch
            {
                RestaurantTier.Platinum => (tierLabels.PlatinumAr, tierLabels.PlatinumEn, tierLabels.PlatinumLogoStorageKey),
                RestaurantTier.Elite => (tierLabels.EliteAr, tierLabels.EliteEn, tierLabels.EliteLogoStorageKey),
                _ => (tierLabels.BasicAr, tierLabels.BasicEn, tierLabels.BasicLogoStorageKey)
            };
            var tierLogo = CatalogImageStorage.ToContentUrl(logoKey);
            var tierName = isArabic ? tierNameAr : tierNameEn;

            return new SubscriptionRestaurantPoolItemDto(
                x.RestaurantId,
                x.NameAr,
                x.NameEn,
                x.Tier,
                x.IsInitiallySelected,
                x.CapturedAtUtc,
                logos.GetValueOrDefault(x.RestaurantId),
                ratingInfo.Count > 0 ? ratingInfo.Avg : null,
                ratingInfo.Count,
                tierNameAr,
                tierNameEn,
                tierName,
                tierLogo);
        }).ToList();

        return new SubscriptionRestaurantPoolDto(
            pool.Id,
            pool.EligibilityCheckedAtUtc,
            pool.RestaurantCount,
            pageReq.NormalizedPage,
            pageReq.NormalizedPageSize,
            totalCount,
            totalPages,
            pageReq.NormalizedPage < totalPages,
            pageReq.NormalizedPage > 1,
            items);
    }

    private async Task<Subscription> SyncPendingPaymentAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subscription.PaymentId) && _sandbox is not null
            && await _sandbox.IsEnabledAsync(cancellationToken)
            && await _sandbox.HasStartedAsync(subscription.Id, cancellationToken)) return subscription;
        var key = subscription.PaymentId;
        var keyType = "PaymentId";
        if (string.IsNullOrWhiteSpace(key) && subscription.FamilyGroupId is Guid familyPaymentGroupId)
        {
            var groupPaymentId = await _db.FamilyGroups.AsNoTracking()
                .Where(x => x.Id == familyPaymentGroupId)
                .Select(x => x.PaymentId)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(groupPaymentId))
                key = groupPaymentId;
        }
        if (string.IsNullOrWhiteSpace(key))
        {
            key = (subscription.FamilyGroupId ?? subscription.Id).ToString();
            keyType = "CustomerReference";
        }

        var gateway = await _payments.GetPaymentStatusAsync(key, keyType, cancellationToken);
        if (gateway is null) return subscription;
        var paymentId = gateway.PaymentId ?? subscription.PaymentId;
        if (string.IsNullOrWhiteSpace(paymentId)) return subscription;

        var paid = gateway.InvoiceStatus.Equals("Paid", StringComparison.OrdinalIgnoreCase)
            || gateway.InvoiceStatus.Equals("Success", StringComparison.OrdinalIgnoreCase);
        var failed = gateway.InvoiceStatus.Equals("Failed", StringComparison.OrdinalIgnoreCase)
            || gateway.InvoiceStatus.Equals("Expired", StringComparison.OrdinalIgnoreCase)
            || gateway.InvoiceStatus.Equals("Canceled", StringComparison.OrdinalIgnoreCase)
            || gateway.InvoiceStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);
        if (!paid && !failed)
            return subscription;

        try
        {
            if (paid)
                await _paymentProcessor.VerifyAndProcessAsync(subscription.Id, paymentId, gateway.InvoiceId, gateway.InvoiceValue, gateway.CurrencyCode, gateway.CustomerReference, cancellationToken: cancellationToken);
            else
                await _paymentProcessor.VerifyAndReleaseFailedAsync(subscription.Id, paymentId, gateway.InvoiceId, gateway.InvoiceValue, gateway.CustomerReference, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == subscription.Id, cancellationToken)
                ?? subscription;
        }

        return await _db.Subscriptions.AsNoTracking().FirstAsync(x => x.Id == subscription.Id, cancellationToken);
    }



    private static bool IsValid(CheckoutAvailabilityRequest r, out string error)
    {
        error = "";
        if (r.ProgramId == Guid.Empty || r.BundleId == Guid.Empty || r.DurationDays < 1 || r.StartDate < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            error = "checkout.invalid_request";
            return false;
        }
        return true;
    }

    private async Task<bool> OwnsSubscriptionAsync(Guid id, Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty) return false;
        var sub = await _db.Subscriptions.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.CustomerUserId, x.FamilyGroupId })
            .FirstOrDefaultAsync(ct);
        if (sub is not null)
        {
            if (sub.CustomerUserId == userId) return true;
            if (sub.FamilyGroupId is Guid familyGroupId)
            {
                return await _db.FamilyGroups.AsNoTracking()
                    .AnyAsync(g => g.Id == familyGroupId && g.ManagerCustomerUserId == userId, ct);
            }
        }
        return false;
    }

    private async Task<bool> OwnsCheckoutTargetAsync(Guid id, Guid userId, CancellationToken ct)
    {
        if (await OwnsSubscriptionAsync(id, userId, ct)) return true;
        if (userId == Guid.Empty) return false;
        if (await _db.FamilyGroups.AsNoTracking().AnyAsync(x => x.Id == id && x.ManagerCustomerUserId == userId, ct))
            return true;

        var member = await _db.FamilyMembers.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new { m.FamilyGroupId, m.MemberSubscriptionId })
            .FirstOrDefaultAsync(ct);
        if (member is not null)
        {
            if (member.MemberSubscriptionId is Guid subId && await OwnsSubscriptionAsync(subId, userId, ct))
                return true;
            return await _db.FamilyGroups.AsNoTracking().AnyAsync(x => x.Id == member.FamilyGroupId && x.ManagerCustomerUserId == userId, ct);
        }
        return false;
    }

    private async Task<Subscription?> ResolveCheckoutSubscriptionAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var subscription = await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (subscription is not null) return subscription;
        if (userId == Guid.Empty) return null;

        var member = await _db.FamilyMembers.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new { m.FamilyGroupId, m.MemberSubscriptionId })
            .FirstOrDefaultAsync(ct);
        if (member?.MemberSubscriptionId is Guid memberSubId)
        {
            var memberSub = await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == memberSubId, ct);
            if (memberSub is not null) return memberSub;
        }

        var groupId = member?.FamilyGroupId ?? id;
        var group = await _db.FamilyGroups.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == groupId && x.ManagerCustomerUserId == userId, ct);
        if (group is null) return null;
        if (group.PaymentAnchorSubscriptionId is Guid anchorId)
        {
            var anchor = await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == anchorId, ct);
            if (anchor is not null) return anchor;
        }

        var pending = await _db.Subscriptions.AsNoTracking()
            .Where(x => x.FamilyGroupId == groupId && x.Status == SubscriptionStatus.PendingPayment)
            .OrderBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (pending is not null) return pending;

        return await _db.Subscriptions.AsNoTracking()
            .Where(x => x.FamilyGroupId == groupId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<FamilyGroup?> FindOwnedFamilyGroupAsync(Guid requestedId, Guid? familyGroupId, Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty) return null;

        var direct = await _db.FamilyGroups.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == requestedId && x.ManagerCustomerUserId == userId, ct);
        if (direct is not null) return direct;
        if (familyGroupId is not Guid groupId || groupId == Guid.Empty) return null;
        return await _db.FamilyGroups.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == groupId && x.ManagerCustomerUserId == userId, ct);
    }

    private bool IsAllowedRedirectUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        var schemes = _configuration?.GetSection("Checkout:AllowedRedirectSchemes").GetChildren().Select(x => x.Value).Where(x => !string.IsNullOrEmpty(x)).ToArray();
        if (schemes is null || schemes.Length == 0) schemes = ["https", "mealmate"];
        if (!schemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)) return false;
        if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) return true;
        var hosts = _configuration?.GetSection("Checkout:AllowedRedirectHosts").GetChildren().Select(x => x.Value).Where(x => !string.IsNullOrEmpty(x)).ToArray();
        if (hosts is null || hosts.Length == 0) hosts = ["maelmate.runasp.net", "localhost"];
        return hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<Guid, string?>> LoadRestaurantLogoUrlsAsync(IReadOnlyCollection<Guid> restaurantIds, CancellationToken ct)
    {
        var ids = restaurantIds.Where(x => x != Guid.Empty).Distinct().ToList();
        var result = ids.ToDictionary(x => x, _ => (string?)null);
        if (ids.Count == 0) return result;

        var documents = await _db.Documents.AsNoTracking()
            .Where(x => ids.Contains(x.OwnerId)
                && x.OwnerType == DocumentOwnerType.Restaurant
                && x.DocumentType == DocumentType.ProfileImage)
            .Select(x => new { x.OwnerId, x.StoragePath })
            .ToListAsync(ct);
        var registrations = await _db.RestaurantRegistrations.AsNoTracking()
            .Where(x => x.ResultingRestaurantId != null && ids.Contains(x.ResultingRestaurantId.Value))
            .Select(x => new { x.Id, RestaurantId = x.ResultingRestaurantId!.Value })
            .ToListAsync(ct);
        var branchOwnerIds = ids.Concat(registrations.Select(x => x.Id)).Distinct().ToList();
        var branchPhotos = await _db.RestaurantBranches.AsNoTracking()
            .Where(x => branchOwnerIds.Contains(x.RestaurantRegistrationId) && x.IsActive)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.NameAr)
            .Select(x => new { x.RestaurantRegistrationId, x.PhotoStorageKey })
            .ToListAsync(ct);

        foreach (var id in ids)
        {
            var logo = documents.FirstOrDefault(x => x.OwnerId == id)?.StoragePath;
            if (string.IsNullOrWhiteSpace(logo))
            {
                var regId = registrations.FirstOrDefault(x => x.RestaurantId == id)?.Id;
                if (regId.HasValue)
                    logo = branchPhotos.FirstOrDefault(x => x.RestaurantRegistrationId == regId.Value)?.PhotoStorageKey;
            }
            if (string.IsNullOrWhiteSpace(logo))
                logo = branchPhotos.FirstOrDefault(x => x.RestaurantRegistrationId == id)?.PhotoStorageKey;

            result[id] = CatalogImageStorage.ToContentUrl(logo);
        }
        return result;
    }
}
