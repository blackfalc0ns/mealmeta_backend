using System.Text.Json;
using MealMate.Api.Common;
using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
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

    public CustomerCheckoutController(IAppDbContext db, ICurrentUser currentUser, IMyFatoorahPaymentService payments, IDateTimeProvider clock, UserManager<ApplicationUser> users, IRestaurantEligibilityService eligibility, ICapacityConcurrencyLock capacityLock, ICheckoutTransactionExecutor transactionExecutor)
        => (_db, _currentUser, _payments, _clock, _users, _eligibility, _capacityLock, _transactionExecutor) = (db, currentUser, payments, clock, users, eligibility, capacityLock, transactionExecutor);

    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(CancellationToken cancellationToken)
    {
        var items = await (from p in _db.Programs.AsNoTracking()
                           join b in _db.Bundles.AsNoTracking() on p.Id equals b.ProgramId
                           where p.IsActive && p.Status == ProgramStatus.Active && b.IsActive && b.Status == ProgramStatus.Active
                           orderby p.SortOrder, b.SortOrder
                           select new CheckoutCatalogItem(p.Id, p.NameAr, p.NameEn, b.Id, b.NameAr, b.NameEn)).ToListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpGet("restaurants")]
    public async Task<IActionResult> Restaurants([FromQuery] CheckoutAvailabilityRequest request, CancellationToken cancellationToken)
    {
        if (!IsValid(request, out var error)) return BadRequest(new { error });
        var pricing = await Sender.Send(new PreviewCustomerPricingQuery(request.ProgramId, request.BundleId, request.Tier, request.DurationDays, IncludeFriday: request.IncludeFriday), cancellationToken);
        if (pricing.IsFailure) return HandleResult(pricing);
        var rows = await _eligibility.EvaluateAsync(new RestaurantEligibilityRequest(request.ProgramId, request.BundleId, request.Tier, request.RegionId, request.StartDate, request.DurationDays, request.IncludeFriday, _currentUser.UserId), cancellationToken);
        return Ok(rows.Where(x => x.IsEligible).Select(x => new EligibleRestaurantDto(x.RestaurantId, x.NameAr, x.NameEn, x.MenuPlanVersionsByDate[0].MenuPlanVersionId, pricing.Value.FinalCustomerPrice)));
    }

    [HttpPost("/api/v1/customer/subscriptions/checkout")]
    public async Task<IActionResult> Checkout([FromBody] StartCheckoutRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return BadRequest(new { error = "checkout.customer_not_found" });
        var availability = new CheckoutAvailabilityRequest(request.ProgramId, request.BundleId, request.Tier, request.DurationDays, request.StartDate, request.RegionId, request.IncludeFriday);
        if (!IsValid(availability, out var error)) return BadRequest(new { error });

        var preview = await Sender.Send(new PreviewCustomerPricingQuery(request.ProgramId, request.BundleId, request.Tier, request.DurationDays, IncludeFriday: request.IncludeFriday), cancellationToken);
        if (preview.IsFailure) return HandleResult(preview);
        var serviceDates = RestaurantEligibilityService.ServiceDates(request.StartDate, request.DurationDays, request.IncludeFriday).ToList();
        var reservation = await _transactionExecutor.ExecuteAsync(async transactionCt =>
        {
            await _capacityLock.AcquireAsync(request.RestaurantId, serviceDates, transactionCt);
            var checkedAt = _clock.UtcNow;
            var eligible = await _eligibility.EvaluateAsync(new RestaurantEligibilityRequest(request.ProgramId, request.BundleId, request.Tier, request.RegionId, request.StartDate, request.DurationDays, request.IncludeFriday, userId, request.RestaurantId), transactionCt);
            var restaurant = eligible.SingleOrDefault(x => x.RestaurantId == request.RestaurantId && x.IsEligible);
            if (restaurant is null) return new CheckoutReservationOutcome(null, null, "restaurant.not_eligible", checkedAt, default);
            var create = await Sender.Send(new CreateSubscriptionCommand(userId, request.ProgramId, request.BundleId, request.Tier, request.DurationDays,
                request.StartDate, request.DeliveryAddress, RegionId: request.RegionId, CorrelationId: request.CorrelationId, IncludeFriday: request.IncludeFriday), transactionCt);
            if (create.IsFailure) return new CheckoutReservationOutcome(null, null, create.Error.Code, checkedAt, default);
            var expiresAt = _clock.UtcNow.AddMinutes(15);
            foreach (var resolved in restaurant.MenuPlanVersionsByDate)
                _db.CapacityHolds.Add(new CapacityHold { RestaurantId = request.RestaurantId, SubscriptionId = create.Value.Id, MenuPlanVersionId = resolved.MenuPlanVersionId, ServiceDate = resolved.ServiceDate, Quantity = 1, Status = CapacityHoldStatus.PendingPayment, ExpiresAtUtc = expiresAt });
            await _db.SaveChangesAsync(transactionCt);
            return new CheckoutReservationOutcome(create.Value, restaurant, null, checkedAt, expiresAt);
        }, cancellationToken);
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
                request.CallbackUrl, request.ErrorUrl, subscriptionId.ToString(), cancellationToken);
        }
        catch
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
                Reason = "MyFatoorah payment initialization failed; capacity holds released.",
                PerformedByUserId = userId, PerformedByUserType = UserType.Customer,
                CorrelationId = request.CorrelationId, TimestampUtc = _clock.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Payment initialization failed", detail: "The capacity reservation was released. Please try checkout again.");
        }
        var priceSnapshotId = await _db.SubscriptionSnapshots.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId).OrderBy(x => x.CapturedAtUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
        return Ok(new CheckoutStartedDto(subscriptionId, paymentUrl, expiresAt, restaurant.MenuPlanVersionsByDate[0].MenuPlanVersionId, preview.Value.FinalCustomerPrice, priceSnapshotId, checkedAt, restaurant.MenuPlanVersionsByDate));
    }

    [HttpGet("/api/v1/customer/subscriptions/{id:guid}/calendar")]
    public async Task<IActionResult> Calendar(Guid id, CancellationToken cancellationToken)
    {
        if (!await OwnsSubscription(id, cancellationToken)) return NotFound();
        var days = await _db.SubscriptionDays.AsNoTracking().Where(x => x.SubscriptionId == id).OrderBy(x => x.ServiceDate)
            .Select(x => new { x.Id, x.ServiceDate, x.RestaurantId, x.Status, x.SelectionDeadlineUtc, x.LockAtUtc, x.ResolvedMenuPlanVersionId,
                x.IsAutoSelected, HasMealSnapshot = _db.SubscriptionDayMealSnapshots.Any(s => s.SubscriptionDayId == x.Id) }).ToListAsync(cancellationToken);
        var dayIds = days.Select(x => x.Id).ToList();
        var selections = await _db.DailyMealSelections.AsNoTracking().Where(x => dayIds.Contains(x.SubscriptionDayId)).OrderBy(x => x.SortOrder).ToListAsync(cancellationToken);
        return Ok(days.Select(x => new { x.Id, x.ServiceDate, x.RestaurantId, x.Status, x.SelectionDeadlineUtc, x.LockAtUtc, x.ResolvedMenuPlanVersionId, x.IsAutoSelected, x.HasMealSnapshot,
            selectedMealIds = selections.Where(s => s.SubscriptionDayId == x.Id).Select(s => s.MealId).ToList() }));
    }

    [HttpPost("/api/v1/customer/subscriptions/{id:guid}/days/{date}/restaurant")]
    public async Task<IActionResult> SetRestaurant(Guid id, DateOnly date, [FromBody] SetDayRestaurantRequest request, CancellationToken cancellationToken)
    {
        if (!await OwnsSubscription(id, cancellationToken)) return NotFound();
        var day = await _db.SubscriptionDays.FirstOrDefaultAsync(x => x.SubscriptionId == id && x.ServiceDate == date, cancellationToken);
        if (day is null || day.Status == SubscriptionDayStatus.Cancelled) return BadRequest(new { error = "calendar.day_unavailable" });
        // A confirmed subscription remains pinned to its checkout restaurant; reassignment needs operational approval.
        if (day.RestaurantId != request.RestaurantId) return BadRequest(new { error = "calendar.restaurant_is_pinned" });
        return Ok(new { day.Id, day.RestaurantId });
    }

    [HttpPost("/api/v1/customer/subscriptions/{id:guid}/days/{date}/meals")]
    public async Task<IActionResult> SetMeals(Guid id, DateOnly date, [FromBody] SetDayMealsRequest request, CancellationToken cancellationToken)
    {
        if (!await OwnsSubscription(id, cancellationToken)) return NotFound();
        var day = await _db.SubscriptionDays.FirstOrDefaultAsync(x => x.SubscriptionId == id && x.ServiceDate == date, cancellationToken);
        if (day is null || day.RestaurantId is null || day.ResolvedMenuPlanVersionId is null || day.Status is SubscriptionDayStatus.Cancelled or SubscriptionDayStatus.Locked72h) return BadRequest(new { error = "calendar.day_unavailable" });
        if (day.SelectionDeadlineUtc is { } deadline && _clock.UtcNow >= deadline) return BadRequest(new { error = "calendar.selection_locked" });
        var programId = await _db.Subscriptions.Where(x => x.Id == id).Select(x => (Guid?)x.ProgramId).FirstOrDefaultAsync(cancellationToken);
        var allowed = await ResolveAvailableMealsAsync(day, programId!.Value, cancellationToken);
        if (request.MealIds.Distinct().Any(x => allowed.All(m => m.MealId != x || m.BlockReason is not null))) return BadRequest(new { error = "calendar.invalid_or_restricted_meal" });
        var selected = allowed.Where(x => request.MealIds.Contains(x.MealId)).ToList();
        if (selected.GroupBy(x => x.Slot, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1)) return BadRequest(new { error = "calendar.multiple_meals_for_slot" });
        var old = await _db.DailyMealSelections.Where(x => x.SubscriptionDayId == day.Id).ToListAsync(cancellationToken);
        _db.DailyMealSelections.RemoveRange(old);
        foreach (var (mealId, index) in request.MealIds.Distinct().Select((mealId, index) => (mealId, index)))
            _db.DailyMealSelections.Add(new MealMate.Domain.CalendarMeals.DailyMealSelection { SubscriptionDayId = day.Id, MealId = mealId, SortOrder = index, SelectedAtUtc = _clock.UtcNow, ResolvedMenuPlanVersionId = day.ResolvedMenuPlanVersionId, ResolvedProgramId = programId });
        day.Status = SubscriptionDayStatus.CustomerSelected;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    [HttpGet("/api/v1/customer/subscriptions/{id:guid}/days/{date}/meals/available")]
    public async Task<IActionResult> AvailableMeals(Guid id, DateOnly date, CancellationToken cancellationToken)
    {
        if (!await OwnsSubscription(id, cancellationToken)) return NotFound();
        var day = await _db.SubscriptionDays.AsNoTracking().FirstOrDefaultAsync(x => x.SubscriptionId == id && x.ServiceDate == date, cancellationToken);
        var programId = await _db.Subscriptions.Where(x => x.Id == id).Select(x => (Guid?)x.ProgramId).FirstOrDefaultAsync(cancellationToken);
        if (day is null || day.ResolvedMenuPlanVersionId is null || programId is null) return BadRequest(new { error = "calendar.day_unavailable" });
        return Ok(await ResolveAvailableMealsAsync(day, programId.Value, cancellationToken));
    }

    [HttpPost("/api/v1/customer/subscriptions/{id:guid}/days/{date}/auto-select")]
    public async Task<IActionResult> AutoSelect(Guid id, DateOnly date, CancellationToken cancellationToken)
    {
        if (!await OwnsSubscription(id, cancellationToken)) return NotFound();
        var day = await _db.SubscriptionDays.FirstOrDefaultAsync(x => x.SubscriptionId == id && x.ServiceDate == date, cancellationToken);
        var programId = await _db.Subscriptions.Where(x => x.Id == id).Select(x => (Guid?)x.ProgramId).FirstOrDefaultAsync(cancellationToken);
        if (day is null || day.ResolvedMenuPlanVersionId is null || programId is null || day.Status is SubscriptionDayStatus.Cancelled or SubscriptionDayStatus.Locked72h) return BadRequest(new { error = "calendar.day_unavailable" });
        var meals = await ResolveAvailableMealsAsync(day, programId.Value, cancellationToken);
        var selections = meals.Where(x => x.BlockReason is null).GroupBy(x => x.Slot).Select(x => x.OrderBy(m => m.SortOrder).First()).ToList();
        if (selections.Count == 0) return BadRequest(new { error = "calendar.no_safe_menu_meals" });
        var old = await _db.DailyMealSelections.Where(x => x.SubscriptionDayId == day.Id).ToListAsync(cancellationToken);
        _db.DailyMealSelections.RemoveRange(old);
        foreach (var (meal, index) in selections.Select((meal, index) => (meal, index)))
            _db.DailyMealSelections.Add(new MealMate.Domain.CalendarMeals.DailyMealSelection { SubscriptionDayId = day.Id, MealId = meal.MealId, SortOrder = index, SelectedAtUtc = _clock.UtcNow, ResolvedMenuPlanVersionId = day.ResolvedMenuPlanVersionId, ResolvedMenuDayId = meal.MenuDayId == Guid.Empty ? null : meal.MenuDayId, ResolvedProgramId = programId });
        day.Status = SubscriptionDayStatus.AutoSelected;
        day.IsAutoSelected = true;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(selections);
    }

    private static bool IsValid(CheckoutAvailabilityRequest r, out string error)
    { error = ""; if (r.ProgramId == Guid.Empty || r.BundleId == Guid.Empty || r.RegionId == Guid.Empty || r.DurationDays < 1 || r.StartDate < DateOnly.FromDateTime(DateTime.UtcNow)) { error = "checkout.invalid_request"; return false; } return true; }
    private Task<bool> OwnsSubscription(Guid id, CancellationToken ct)
        => _currentUser.UserId is { } userId
            ? _db.Subscriptions.AsNoTracking().AnyAsync(x => x.Id == id && x.CustomerUserId == userId, ct)
            : Task.FromResult(false);

    private async Task<List<AvailableDayMealDto>> ResolveAvailableMealsAsync(MealMate.Domain.CalendarMeals.SubscriptionDay day, Guid programId, CancellationToken ct)
    {
        var frozen = await _db.SubscriptionDayMealSnapshots.AsNoTracking().FirstOrDefaultAsync(x => x.SubscriptionDayId == day.Id, ct);
        if (frozen is not null)
        {
            var meals = JsonSerializer.Deserialize<List<MealSnapshotItem>>(frozen.MealsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            var frozenUserId = _currentUser.UserId ?? Guid.Empty;
            var choices = await _db.CustomerAllergyPreferences.AsNoTracking().Where(x => x.CustomerUserId == frozenUserId).ToListAsync(ct);
            var allergens = choices.Where(x => !x.IsDislike).Select(x => FoodCodeNormalizer.Normalize(x.AllergenCode)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var disliked = choices.Where(x => x.IsDislike && x.AllergenCode.StartsWith("INGREDIENT:", StringComparison.OrdinalIgnoreCase)).Select(x => x.AllergenCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var dietary = await _db.CustomerDietaryPreferences.AsNoTracking().Where(x => x.CustomerUserId == frozenUserId).Select(x => x.PreferenceCode).ToListAsync(ct);
            return meals.Select(x =>
            {
                var effective = x.PresentAllergenCodes.Concat(x.MayContainAllergenCodes).Concat(x.Ingredients.SelectMany(i => i.AllergenCodes)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var reason = effective.Overlaps(allergens) ? "allergy_conflict"
                    : x.Ingredients.Any(i => disliked.Contains($"INGREDIENT:{i.Id}")) ? "ingredient_preference_conflict"
                    : !dietary.All(code => x.DietaryPreferenceCodes.Contains(code, StringComparer.OrdinalIgnoreCase)) ? "dietary_preference_conflict" : null;
                var customerIngredients = x.Ingredients.Select(i => new CustomerMealIngredientDto(i.Id, i.NameAr, i.NameEn,
                    i.IsOriginVisibleToCustomers ? i.OriginCountryCode : null, i.IsOriginVisibleToCustomers ? i.SupplierName : null,
                    i.IsLocal, i.IsHalalCertified, i.AllergenCodes)).ToList();
                return new AvailableDayMealDto(x.MealId, Guid.Empty, x.NameAr, x.NameEn, x.Slot, x.SortOrder, reason,
                    x.DescriptionAr, x.DescriptionEn, x.ImageUrl, x.Calories, x.ProteinG, x.CarbsG, x.FatG,
                    x.PresentAllergenCodes, x.MayContainAllergenCodes, x.DietaryPreferenceCodes, customerIngredients);
            }).ToList();
        }
        // Legacy subscriptions that cannot be backfilled are reported to operations.
        // Never guess from the restaurant's mutable live menu.
        return [];
    }
}

public sealed record CheckoutCatalogItem(Guid ProgramId, string ProgramNameAr, string ProgramNameEn, Guid BundleId, string BundleNameAr, string BundleNameEn);
public sealed record CheckoutAvailabilityRequest(Guid ProgramId, Guid BundleId, RestaurantTier Tier, int DurationDays, DateOnly StartDate, Guid RegionId, bool IncludeFriday = false);
public sealed record StartCheckoutRequest(Guid RestaurantId, Guid ProgramId, Guid BundleId, RestaurantTier Tier, int DurationDays, DateOnly StartDate, Guid RegionId, string? DeliveryAddress, string CallbackUrl, string ErrorUrl, bool IncludeFriday = false, string? CorrelationId = null);
public sealed record EligibleRestaurantDto(Guid RestaurantId, string NameAr, string NameEn, Guid MenuPlanVersionId, decimal FinalPrice);
public sealed record CheckoutStartedDto(Guid SubscriptionId, string PaymentUrl, DateTime HoldExpiresAtUtc, Guid MenuPlanVersionId, decimal FinalPrice, Guid? PriceSnapshotId = null, DateTime? EligibilityCheckedAtUtc = null, IReadOnlyList<EligibleMenuDateDto>? MenuPlanVersionsByDate = null);
public sealed record SetDayRestaurantRequest(Guid RestaurantId);
public sealed record SetDayMealsRequest(IReadOnlyList<Guid> MealIds);
public sealed record CustomerMealIngredientDto(Guid Id, string NameAr, string NameEn, string? OriginCountryCode, string? SupplierName, bool IsLocal, bool IsHalalCertified, IReadOnlyList<string> AllergenCodes);
public sealed record AvailableDayMealDto(Guid MealId, Guid MenuDayId, string NameAr, string NameEn, string Slot, int SortOrder, string? BlockReason,
    string? DescriptionAr = null, string? DescriptionEn = null, string? ImageUrl = null, decimal? Calories = null, decimal? ProteinG = null,
    decimal? CarbsG = null, decimal? FatG = null, IReadOnlyList<string>? PresentAllergenCodes = null, IReadOnlyList<string>? MayContainAllergenCodes = null,
    IReadOnlyList<string>? DietaryPreferenceCodes = null, IReadOnlyList<CustomerMealIngredientDto>? Ingredients = null);
internal sealed record CheckoutReservationOutcome(SubscriptionDto? Subscription, RestaurantEligibilityDto? Restaurant, string? ErrorCode, DateTime CheckedAtUtc, DateTime ExpiresAtUtc);
