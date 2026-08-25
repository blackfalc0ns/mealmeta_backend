using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Controllers.V1;

/// <summary>
/// Customer checkout is deliberately backed by one eligibility predicate.  A restaurant
/// returned by this controller is the only kind that may be reserved at payment time.
/// </summary>
[Authorize]
[Route("api/v1/customer/checkout")]
public sealed class CustomerCheckoutController : ApiControllerBase
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IMyFatoorahPaymentService _payments;
    private readonly IDateTimeProvider _clock;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IRestaurantEligibilityService _eligibility;
    private readonly ICapacityConcurrencyLock _capacityLock;
    private readonly ICheckoutTransactionExecutor _transactionExecutor;
    private readonly ISubscriptionScheduleService _schedule;
    private readonly IRestaurantPoolSnapshotService _restaurantPool;
    private readonly IConfiguration _configuration;
    private readonly IAllowedSubscriptionDurationService _allowedDurations;
    private readonly ISubscriptionPaymentProcessor _paymentProcessor;
    private readonly ISubscriptionCheckoutReadinessService _readiness;

    public CustomerCheckoutController(IAppDbContext db, ICurrentUser currentUser, IMyFatoorahPaymentService payments, IDateTimeProvider clock, UserManager<ApplicationUser> users, IRestaurantEligibilityService eligibility, ICapacityConcurrencyLock capacityLock, ICheckoutTransactionExecutor transactionExecutor, ISubscriptionScheduleService schedule, IRestaurantPoolSnapshotService restaurantPool, IConfiguration configuration, IAllowedSubscriptionDurationService allowedDurations, ISubscriptionPaymentProcessor paymentProcessor, ISubscriptionCheckoutReadinessService readiness)
        => (_db, _currentUser, _payments, _clock, _users, _eligibility, _capacityLock, _transactionExecutor, _schedule, _restaurantPool, _configuration, _allowedDurations, _paymentProcessor, _readiness) = (db, currentUser, payments, clock, users, eligibility, capacityLock, transactionExecutor, schedule, restaurantPool, configuration, allowedDurations, paymentProcessor, readiness);

    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(CancellationToken cancellationToken)
    {
        if (_currentUser.UserType != UserType.Customer) return Forbid();
        var items = await (from p in _db.Programs.AsNoTracking()
                           join b in _db.Bundles.AsNoTracking() on p.Id equals b.ProgramId
                           where p.IsActive && p.Status == ProgramStatus.Active && b.IsActive && b.Status == ProgramStatus.Active
                           orderby p.SortOrder, b.SortOrder
                           select new CheckoutCatalogItem(p.Id, p.NameAr, p.NameEn, b.Id, b.NameAr, b.NameEn)).ToListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpGet("restaurants")]
    [ProducesResponseType(typeof(IReadOnlyList<EligibleRestaurantDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Restaurants([FromQuery] CheckoutAvailabilityRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserType != UserType.Customer) return Forbid();
        if (!IsValid(request, out var error)) return BadRequest(new { error });
        var durationCheck = await _allowedDurations.EvaluateAsync(request.DurationDays, request.IncludeFriday, cancellationToken);
        if (!durationCheck.IsAllowed) return BadRequest(new { error = durationCheck.ErrorCode ?? "subscription_flow.duration_not_available" });
        var serviceDates = _schedule.GetServiceDates(request.StartDate, request.DurationDays, request.IncludeFriday);
        var fridayDays = _schedule.CountFridays(serviceDates);
        var pricing = await Sender.Send(new PreviewCustomerPricingQuery(request.ProgramId, request.BundleId, request.Tier, request.DurationDays,
            IncludeFriday: request.IncludeFriday, FridayDays: fridayDays), cancellationToken);
        if (pricing.IsFailure) return HandleResult(pricing);
        var rows = await _eligibility.EvaluateAsync(new RestaurantEligibilityRequest(request.ProgramId, request.BundleId, request.Tier, request.RegionId, request.StartDate, request.DurationDays, request.IncludeFriday, _currentUser.UserId), cancellationToken);
        return Ok(rows.Where(x => x.IsEligible).Select(x => new EligibleRestaurantDto(x.RestaurantId, x.NameAr, x.NameEn,
            x.MenuPlanVersionsByDate[0].MenuPlanVersionId, pricing.Value.FinalCustomerPrice, request.IncludeFriday, fridayDays, serviceDates.Count)));
    }

    [HttpPost("/api/v1/customer/subscriptions/checkout")]
    [ProducesResponseType(typeof(CheckoutStartedDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Checkout([FromBody] StartCheckoutRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();
        if (_currentUser.UserType != UserType.Customer) return Forbid();
        if (!IsAllowedRedirectUrl(request.CallbackUrl) || !IsAllowedRedirectUrl(request.ErrorUrl))
            return BadRequest(new { error = "checkout.redirect_url_not_allowed" });
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return BadRequest(new { error = "checkout.customer_not_found" });
        var gate = await _readiness.EvaluateAsync(userId, request.ProgramId, request.BundleId, request.Tier, request.DurationDays, request.IncludeFriday, cancellationToken);
        if (gate.IsFailure)
            return gate.Error.Type == ErrorType.NotFound
                ? NotFound(new { error = gate.Error.Code })
                : BadRequest(new { error = gate.Error.Code });
        var profileBlock = SubscriptionCheckoutGateCodes.ProfileCheckoutBlockCode(gate.Value);
        if (profileBlock is not null)
            return Conflict(new { error = profileBlock });
        if (gate.Value.CheckoutBlockReason == SubscriptionCheckoutGateCodes.ExistingSubscription)
            return Conflict(new { error = "checkout.existing_subscription", subscriptionId = gate.Value.BlockingSubscriptionId, status = gate.Value.BlockingSubscriptionStatus });
        if (gate.Value.Address is null)
            return Conflict(new { error = "checkout.address_required" });
        var regionId = gate.Value.Address.RegionId;
        if (!_schedule.TryResolveCheckoutStart(request.StartDate, request.IncludeFriday, out var startDate, out var startError))
            return BadRequest(new { error = startError });
        var chosenStartDate = request.StartDate;
        var deliveryAddress = gate.Value.Address.AddressLine;
        var availability = new CheckoutAvailabilityRequest(request.ProgramId, request.BundleId, request.Tier, request.DurationDays, startDate, regionId, request.IncludeFriday);
        if (!IsValid(availability, out var error)) return BadRequest(new { error });
        var durationCheck = await _allowedDurations.EvaluateAsync(request.DurationDays, request.IncludeFriday, cancellationToken);
        if (!durationCheck.IsAllowed) return BadRequest(new { error = durationCheck.ErrorCode ?? "subscription_flow.duration_not_available" });

        var serviceDates = _schedule.GetServiceDates(startDate, request.DurationDays, request.IncludeFriday);
        var fridayDays = _schedule.CountFridays(serviceDates);
        var preview = await Sender.Send(new PreviewCustomerPricingQuery(request.ProgramId, request.BundleId, request.Tier, request.DurationDays,
            IncludeFriday: request.IncludeFriday, FridayDays: fridayDays), cancellationToken);
        if (preview.IsFailure) return HandleResult(preview);
        CheckoutReservationOutcome reservation;
        try
        {
            reservation = await _transactionExecutor.ExecuteAsync(async transactionCt =>
            {
                var checkedAt = _clock.UtcNow;
                var allRows = await _eligibility.EvaluateAsync(new RestaurantEligibilityRequest(request.ProgramId, request.BundleId, request.Tier, regionId, startDate, request.DurationDays, request.IncludeFriday, userId), transactionCt);
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
                var create = await Sender.Send(new CreateSubscriptionCommand(userId, request.ProgramId, request.BundleId, request.Tier, request.DurationDays,
                    startDate, deliveryAddress, RegionId: resolvedRegionId, CorrelationId: request.CorrelationId, IncludeFriday: request.IncludeFriday), transactionCt);
                if (create.IsFailure) throw new CheckoutReservationException(create.Error.Code);
                var expiresAt = _clock.UtcNow.AddMinutes(15);
                foreach (var resolved in restaurant.MenuPlanVersionsByDate)
                    _db.CapacityHolds.Add(new CapacityHold { RestaurantId = restaurant.RestaurantId, SubscriptionId = create.Value.Id, MenuPlanVersionId = resolved.MenuPlanVersionId, ServiceDate = resolved.ServiceDate, Quantity = 1, Status = CapacityHoldStatus.PendingPayment, ExpiresAtUtc = expiresAt });
                await _db.SaveChangesAsync(transactionCt);
                var pool = await _restaurantPool.CaptureAsync(create.Value.Id, eligible, restaurant.RestaurantId, transactionCt);
                if (!pool.IsSuccess) throw new CheckoutReservationException(pool.ErrorCode ?? "checkout.restaurant_pool_failed");
                return new CheckoutReservationOutcome(create.Value, restaurant, null, checkedAt, expiresAt, pool.PoolSnapshotId, pool.RestaurantCount, resolvedRegionId);
            }, cancellationToken);
        }
        catch (CheckoutReservationException ex)
        {
            return BadRequest(new { error = ex.Code, message = "Checkout eligibility or restaurant snapshot changed. Refresh and try again." });
        }
        if (reservation.ErrorCode is not null) return BadRequest(new { error = reservation.ErrorCode, message = "Checkout eligibility or pricing changed. Refresh and try again." });
        var create = reservation.Subscription!;
        var restaurant = reservation.Restaurant!;
        var subscriptionId = create.Id;
        var expiresAt = reservation.ExpiresAtUtc;
        var checkedAt = reservation.CheckedAtUtc;

        string paymentUrl;
        try
        {
            paymentUrl = await _payments.ExecutePaymentAsync(create.PaidAmount, user.FullName ?? "MealMate Customer", user.Email ?? "", user.PhoneNumber ?? "",
                request.CallbackUrl, request.ErrorUrl, subscriptionId.ToString(), request.PaymentMethodId, cancellationToken);
        }
        catch (Exception ex)
        {
            var failedSubscription = await _db.Subscriptions.FirstAsync(x => x.Id == subscriptionId, cancellationToken);
            failedSubscription.Status = SubscriptionStatus.PaymentFailed;
            failedSubscription.LastOperationAtUtc = _clock.UtcNow;
            var failedHolds = await _db.CapacityHolds.Where(x => x.SubscriptionId == subscriptionId && x.Status == CapacityHoldStatus.PendingPayment).ToListAsync(cancellationToken);
            foreach (var hold in failedHolds) { hold.Status = CapacityHoldStatus.Released; hold.ReleasedAtUtc = _clock.UtcNow; }
            _db.SubscriptionOperations.Add(new SubscriptionOperation
            {
                SubscriptionId = subscriptionId, OperationType = "PAYMENT_INITIALIZATION_FAILED",
                PreviousStatus = SubscriptionStatus.PendingPayment, NewStatus = SubscriptionStatus.PaymentFailed,
                Reason = ex.Message,
                PerformedByUserId = userId, PerformedByUserType = UserType.Customer,
                CorrelationId = request.CorrelationId, TimestampUtc = _clock.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Payment initialization failed", detail: ex.Message);
        }
        var gatewayPaymentId = PaymentIdFromUrl(paymentUrl);
        if (!string.IsNullOrWhiteSpace(gatewayPaymentId))
        {
            var pending = await _db.Subscriptions.FirstAsync(x => x.Id == subscriptionId, cancellationToken);
            pending.PaymentId = gatewayPaymentId;
            await _db.SaveChangesAsync(cancellationToken);
        }
        var priceSnapshotId = await _db.SubscriptionSnapshots.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId).OrderBy(x => x.CapturedAtUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
        return Ok(new CheckoutStartedDto(subscriptionId, paymentUrl, expiresAt, restaurant.MenuPlanVersionsByDate[0].MenuPlanVersionId,
            create.PaidAmount, priceSnapshotId, checkedAt, restaurant.MenuPlanVersionsByDate, reservation.PoolSnapshotId,
            reservation.RestaurantCount, "PendingPayment", "PendingPayment", request.IncludeFriday, fridayDays, serviceDates.Count,
            restaurant.RestaurantId, reservation.RegionId, startDate, deliveryAddress, chosenStartDate));
    }

    [HttpGet("/api/v1/customer/subscriptions/{id:guid}/payment")]
    [ProducesResponseType(typeof(SubscriptionPaymentStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> PaymentStatus(Guid id, CancellationToken cancellationToken)
    {
        if (!await OwnsSubscription(id, cancellationToken)) return NotFound();
        var subscription = await _db.Subscriptions.AsNoTracking().FirstAsync(x => x.Id == id, cancellationToken);
        if (subscription.Status == SubscriptionStatus.PendingPayment)
            subscription = await SyncPendingPaymentAsync(subscription, cancellationToken);
        var isPaid = subscription.Status is SubscriptionStatus.Paid or SubscriptionStatus.Scheduled or SubscriptionStatus.Active;
        var isFailed = subscription.Status is SubscriptionStatus.PaymentFailed or SubscriptionStatus.Cancelled;
        return Ok(new SubscriptionPaymentStatusDto(id, subscription.Status.ToString(), isPaid, isFailed, subscription.PaidAmount.Amount, subscription.PaidAmount.CurrencyCode));
    }

    private async Task<Subscription> SyncPendingPaymentAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        var key = subscription.PaymentId;
        var keyType = "PaymentId";
        if (string.IsNullOrWhiteSpace(key))
        {
            key = subscription.Id.ToString();
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
        if (paid)
            await _paymentProcessor.VerifyAndProcessAsync(subscription.Id, paymentId, gateway.InvoiceId, gateway.InvoiceValue, gateway.CurrencyCode, gateway.CustomerReference, cancellationToken: cancellationToken);
        else if (failed)
            await _paymentProcessor.VerifyAndReleaseFailedAsync(subscription.Id, paymentId, gateway.InvoiceId, gateway.InvoiceValue, gateway.CustomerReference, cancellationToken: cancellationToken);
        else
            return subscription;

        return await _db.Subscriptions.AsNoTracking().FirstAsync(x => x.Id == subscription.Id, cancellationToken);
    }

    private static string? PaymentIdFromUrl(string paymentUrl)
    {
        if (!Uri.TryCreate(paymentUrl, UriKind.Absolute, out var uri)) return null;
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0) continue;
            if (part[..separator].Equals("paymentId", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(part[(separator + 1)..]);
        }
        return null;
    }

    [HttpGet("/api/v1/customer/subscriptions/{id:guid}/restaurants")]
    [ProducesResponseType(typeof(SubscriptionRestaurantPoolDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SubscriptionRestaurants(Guid id, CancellationToken cancellationToken)
    {
        if (!await OwnsSubscription(id, cancellationToken)) return NotFound();
        var pool = await _db.SubscriptionRestaurantPoolSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriptionId == id, cancellationToken);
        if (pool is null) return Conflict(new { error = "subscription.restaurant_pool_not_ready" });
        var restaurants = await _db.SubscriptionRestaurantSnapshots.AsNoTracking()
            .Where(x => x.SubscriptionId == id)
            .OrderByDescending(x => x.IsInitiallySelected).ThenBy(x => x.NameEn)
            .Select(x => new SubscriptionRestaurantPoolItemDto(x.RestaurantId, x.NameAr, x.NameEn, x.Tier, x.IsInitiallySelected, x.CapturedAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(new SubscriptionRestaurantPoolDto(pool.Id, pool.EligibilityCheckedAtUtc, pool.RestaurantCount, restaurants));
    }

    private static bool IsValid(CheckoutAvailabilityRequest r, out string error)
    { error = ""; if (r.ProgramId == Guid.Empty || r.BundleId == Guid.Empty || r.DurationDays < 1 || r.StartDate < DateOnly.FromDateTime(DateTime.UtcNow)) { error = "checkout.invalid_request"; return false; } return true; }
    private Task<bool> OwnsSubscription(Guid id, CancellationToken ct)
        => _currentUser.UserType == UserType.Customer && _currentUser.UserId is { } userId
            ? _db.Subscriptions.AsNoTracking().AnyAsync(x => x.Id == id && x.CustomerUserId == userId, ct)
            : Task.FromResult(false);

    private bool IsAllowedRedirectUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        var schemes = _configuration.GetSection("Checkout:AllowedRedirectSchemes").Get<string[]>() ?? ["https", "mealmate"];
        if (!schemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)) return false;
        if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) return true;
        var hosts = _configuration.GetSection("Checkout:AllowedRedirectHosts").Get<string[]>() ?? ["maelmate.runasp.net", "localhost"];
        return hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record CheckoutCatalogItem(Guid ProgramId, string ProgramNameAr, string ProgramNameEn, Guid BundleId, string BundleNameAr, string BundleNameEn);
public sealed record CheckoutAvailabilityRequest(Guid ProgramId, Guid BundleId, RestaurantTier Tier, int DurationDays, DateOnly StartDate, Guid RegionId, bool IncludeFriday = false);
public sealed record StartCheckoutRequest(
    Guid ProgramId,
    Guid BundleId,
    RestaurantTier Tier,
    int DurationDays,
    string CallbackUrl,
    string ErrorUrl,
    DateOnly StartDate,
    bool IncludeFriday = false,
    string? CorrelationId = null,
    int PaymentMethodId = 1);
public sealed record EligibleRestaurantDto(Guid RestaurantId, string NameAr, string NameEn, Guid MenuPlanVersionId, decimal FinalPrice,
    bool IncludeFriday, int FridayDays, int TotalServiceDays);
public sealed record CheckoutStartedDto(Guid SubscriptionId, string PaymentUrl, DateTime HoldExpiresAtUtc, Guid MenuPlanVersionId, decimal FinalPrice, Guid? PriceSnapshotId = null, DateTime? EligibilityCheckedAtUtc = null, IReadOnlyList<EligibleMenuDateDto>? MenuPlanVersionsByDate = null, Guid? RestaurantPoolSnapshotId = null, int RestaurantSnapshotCount = 0, string? PaymentStatus = null, string? FulfillmentStatus = null, bool IncludeFriday = false, int FridayDays = 0, int TotalServiceDays = 0, Guid? RestaurantId = null, Guid? RegionId = null, DateOnly? StartDate = null, string? DeliveryAddress = null, DateOnly? ChosenStartDate = null);
public sealed record SubscriptionPaymentStatusDto(Guid SubscriptionId, string PaymentStatus, bool IsPaid, bool IsFailed, decimal Amount, string CurrencyCode);
public sealed record SubscriptionRestaurantPoolItemDto(Guid RestaurantId, string NameAr, string NameEn, RestaurantTier Tier, bool IsInitiallySelected, DateTime CapturedAtUtc);
public sealed record SubscriptionRestaurantPoolDto(Guid RestaurantPoolSnapshotId, DateTime EligibilityCheckedAtUtc, int RestaurantCount, IReadOnlyList<SubscriptionRestaurantPoolItemDto> Restaurants);
internal sealed record CheckoutReservationOutcome(SubscriptionDto? Subscription, RestaurantEligibilityDto? Restaurant, string? ErrorCode, DateTime CheckedAtUtc, DateTime ExpiresAtUtc, Guid? PoolSnapshotId = null, int RestaurantCount = 0, Guid RegionId = default);
internal sealed class CheckoutReservationException(string code) : Exception(code) { public string Code { get; } = code; }
