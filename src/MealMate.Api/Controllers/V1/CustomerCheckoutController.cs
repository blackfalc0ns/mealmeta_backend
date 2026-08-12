using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
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

    public CustomerCheckoutController(IAppDbContext db, ICurrentUser currentUser, IMyFatoorahPaymentService payments, IDateTimeProvider clock, UserManager<ApplicationUser> users)
        => (_db, _currentUser, _payments, _clock, _users) = (db, currentUser, payments, clock, users);

    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(CancellationToken cancellationToken)
    {
        var items = await (from p in _db.Programs.AsNoTracking()
                           join b in _db.Bundles.AsNoTracking() on p.Id equals b.ProgramId
                           where p.IsActive && b.IsActive
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
        return Ok(await ResolveEligibleAsync(request, pricing.Value.FinalCustomerPrice, cancellationToken));
    }

    [HttpPost("/api/v1/customer/subscriptions/checkout")]
    public async Task<IActionResult> Checkout([FromBody] StartCheckoutRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();
        var availability = new CheckoutAvailabilityRequest(request.ProgramId, request.BundleId, request.Tier, request.DurationDays, request.StartDate, request.RegionId, request.IncludeFriday);
        if (!IsValid(availability, out var error)) return BadRequest(new { error });

        var preview = await Sender.Send(new PreviewCustomerPricingQuery(request.ProgramId, request.BundleId, request.Tier, request.DurationDays, IncludeFriday: request.IncludeFriday), cancellationToken);
        if (preview.IsFailure) return HandleResult(preview);
        var eligible = await ResolveEligibleAsync(availability, preview.Value.FinalCustomerPrice, cancellationToken);
        var restaurant = eligible.SingleOrDefault(x => x.RestaurantId == request.RestaurantId);
        if (restaurant is null) return BadRequest(new { error = "restaurant.not_eligible", message = "The selected restaurant no longer meets checkout eligibility." });

        var create = await Sender.Send(new CreateSubscriptionCommand(userId, request.ProgramId, request.BundleId, request.Tier, request.DurationDays,
            request.StartDate, request.DeliveryAddress, RegionId: request.RegionId, CorrelationId: request.CorrelationId, IncludeFriday: request.IncludeFriday), cancellationToken);
        if (create.IsFailure) return HandleResult(create);

        var subscriptionId = create.Value.Id;
        var expiresAt = _clock.UtcNow.AddMinutes(15);
        foreach (var date in ServiceDates(request.StartDate, request.DurationDays, request.IncludeFriday))
            _db.CapacityHolds.Add(new CapacityHold { RestaurantId = request.RestaurantId, SubscriptionId = subscriptionId, MenuPlanVersionId = restaurant.MenuPlanVersionId, ServiceDate = date, Quantity = 1, Status = CapacityHoldStatus.PendingPayment, ExpiresAtUtc = expiresAt });
        await _db.SaveChangesAsync(cancellationToken);

        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return BadRequest(new { error = "checkout.customer_not_found" });
        var paymentUrl = await _payments.ExecutePaymentAsync(create.Value.PaidAmount, user.FullName ?? "MealMate Customer", user.Email ?? "", user.PhoneNumber ?? "",
            request.CallbackUrl, request.ErrorUrl, subscriptionId.ToString(), cancellationToken);
        return Ok(new CheckoutStartedDto(subscriptionId, paymentUrl, expiresAt, restaurant.MenuPlanVersionId, preview.Value.FinalCustomerPrice));
    }

    [HttpGet("/api/v1/customer/subscriptions/{id:guid}/calendar")]
    public async Task<IActionResult> Calendar(Guid id, CancellationToken cancellationToken)
    {
        if (!await OwnsSubscription(id, cancellationToken)) return NotFound();
        var days = await _db.SubscriptionDays.AsNoTracking().Where(x => x.SubscriptionId == id).OrderBy(x => x.ServiceDate)
            .Select(x => new { x.Id, x.ServiceDate, x.RestaurantId, x.Status, x.SelectionDeadlineUtc, x.LockAtUtc }).ToListAsync(cancellationToken);
        return Ok(days);
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
            _db.DailyMealSelections.Add(new MealMate.Domain.CalendarMeals.DailyMealSelection { SubscriptionDayId = day.Id, MealId = meal.MealId, SortOrder = index, SelectedAtUtc = _clock.UtcNow, ResolvedMenuPlanVersionId = day.ResolvedMenuPlanVersionId, ResolvedMenuDayId = meal.MenuDayId, ResolvedProgramId = programId });
        day.Status = SubscriptionDayStatus.AutoSelected;
        day.IsAutoSelected = true;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(selections);
    }

    private async Task<List<EligibleRestaurantDto>> ResolveEligibleAsync(CheckoutAvailabilityRequest request, decimal price, CancellationToken ct)
    {
        var dates = ServiceDates(request.StartDate, request.DurationDays, request.IncludeFriday).ToList();
        var candidates = await (from r in _db.RestaurantProfiles.AsNoTracking()
                                join offer in _db.RestaurantProgramBundleOffers.AsNoTracking() on r.Id equals offer.RestaurantId
                                join region in _db.RestaurantServiceRegions.AsNoTracking() on r.Id equals region.RestaurantId
                                where r.VerificationStatus == VerificationStatus.Approved && r.IsOperationallyActive
                                   && offer.IsActive && offer.ProgramId == request.ProgramId && offer.BundleId == request.BundleId
                                   && region.IsActive && region.RegionId == request.RegionId
                                select new { r.Id, r.TradeNameAr, r.TradeNameEn }).Distinct().ToListAsync(ct);
        var menuVersions = await (from v in _db.MenuPlanVersions.AsNoTracking()
                                  join p in _db.MenuPlans.AsNoTracking() on v.MenuPlanId equals p.Id
                                  join pp in _db.MenuPlanPrograms.AsNoTracking() on p.Id equals pp.MenuPlanId
                                  where p.RestaurantId != null && pp.ProgramId == request.ProgramId
                                     && pp.EffectiveFrom <= request.StartDate && (pp.EffectiveTo == null || pp.EffectiveTo >= request.StartDate)
                                     && v.Status == VerificationStatus.Approved && !v.IsPaused && v.EffectiveFrom <= request.StartDate
                                  select new { RestaurantId = p.RestaurantId!.Value, VersionId = v.Id, v.EffectiveFrom }).ToListAsync(ct);
        var caps = await _db.DailyCapacities.AsNoTracking().Where(x => candidates.Select(c => c.Id).Contains(x.RestaurantId) && dates.Contains(x.ServiceDate)).ToListAsync(ct);
        var schedules = await _db.RestaurantOperatingSchedules.AsNoTracking().Where(x => candidates.Select(c => c.Id).Contains(x.RestaurantId)).ToListAsync(ct);
        var exceptions = await _db.RestaurantOperatingDayExceptions.AsNoTracking().Where(x => candidates.Select(c => c.Id).Contains(x.RestaurantId) && dates.Contains(x.Date)).ToListAsync(ct);
        var busy = await _db.BusyStatuses.AsNoTracking().Where(x => candidates.Select(c => c.Id).Contains(x.RestaurantId) && x.IsBusy).ToListAsync(ct);
        var confirmed = await _db.SubscriptionDays.AsNoTracking().Where(x => x.RestaurantId != null && candidates.Select(c => c.Id).Contains(x.RestaurantId.Value) && dates.Contains(x.ServiceDate) && x.Status != SubscriptionDayStatus.Cancelled)
            .GroupBy(x => new { x.RestaurantId, x.ServiceDate }).Select(x => new { x.Key.RestaurantId, x.Key.ServiceDate, Count = x.Count() }).ToListAsync(ct);
        var held = await _db.CapacityHolds.AsNoTracking().Where(x => candidates.Select(c => c.Id).Contains(x.RestaurantId) && dates.Contains(x.ServiceDate) && (x.Status == CapacityHoldStatus.Confirmed || (x.Status == CapacityHoldStatus.PendingPayment && x.ExpiresAtUtc > _clock.UtcNow)))
            .GroupBy(x => new { x.RestaurantId, x.ServiceDate }).Select(x => new { x.Key.RestaurantId, x.Key.ServiceDate, Count = x.Sum(h => h.Quantity) }).ToListAsync(ct);

        return candidates.Select(c =>
        {
            var version = menuVersions.Where(x => x.RestaurantId == c.Id).OrderByDescending(x => x.EffectiveFrom).FirstOrDefault();
            if (version is null) return null;
            var available = dates.All(d =>
            {
                var exception = exceptions.FirstOrDefault(x => x.RestaurantId == c.Id && x.Date == d);
                var scheduled = schedules.Where(x => x.RestaurantId == c.Id).ToList();
                var isWorking = exception?.IsWorkingDay ?? (scheduled.Count == 0 || scheduled.Any(x => x.DayOfWeek == d.DayOfWeek && x.IsWorkingDay));
                var isBusy = busy.Any(x => x.RestaurantId == c.Id && x.StartsAtUtc.Date <= d.ToDateTime(TimeOnly.MinValue).Date && (x.EndsAtUtc == null || x.EndsAtUtc.Value.Date >= d.ToDateTime(TimeOnly.MinValue).Date));
                if (!isWorking || isBusy) return false;
                var limit = caps.Where(x => x.RestaurantId == c.Id && x.ServiceDate == d && x.CapacityType == CapacityType.DateOverride).Select(x => (int?)x.MaxBoxes).FirstOrDefault()
                    ?? caps.Where(x => x.RestaurantId == c.Id && x.CapacityType == CapacityType.Base && x.ServiceDate <= d).OrderByDescending(x => x.ServiceDate).Select(x => (int?)x.MaxBoxes).FirstOrDefault() ?? 40;
                var used = confirmed.Where(x => x.RestaurantId == c.Id && x.ServiceDate == d).Select(x => x.Count).FirstOrDefault() + held.Where(x => x.RestaurantId == c.Id && x.ServiceDate == d).Select(x => x.Count).FirstOrDefault();
                return used < limit;
            });
            return available ? new EligibleRestaurantDto(c.Id, c.TradeNameAr, c.TradeNameEn, version.VersionId, price) : null;
        }).Where(x => x is not null).Cast<EligibleRestaurantDto>().ToList();
    }

    private static IEnumerable<DateOnly> ServiceDates(DateOnly start, int count, bool includeFriday)
    { for (var d = start; count > 0; d = d.AddDays(1)) if (includeFriday || d.DayOfWeek != DayOfWeek.Friday) { yield return d; count--; } }
    private static bool IsValid(CheckoutAvailabilityRequest r, out string error)
    { error = ""; if (r.ProgramId == Guid.Empty || r.BundleId == Guid.Empty || r.RegionId == Guid.Empty || r.DurationDays < 1 || r.StartDate < DateOnly.FromDateTime(DateTime.UtcNow)) { error = "checkout.invalid_request"; return false; } return true; }
    private Task<bool> OwnsSubscription(Guid id, CancellationToken ct)
        => _currentUser.UserId is { } userId
            ? _db.Subscriptions.AsNoTracking().AnyAsync(x => x.Id == id && x.CustomerUserId == userId, ct)
            : Task.FromResult(false);

    private async Task<List<AvailableDayMealDto>> ResolveAvailableMealsAsync(MealMate.Domain.CalendarMeals.SubscriptionDay day, Guid programId, CancellationToken ct)
    {
        var menuDays = await _db.MenuDays.AsNoTracking().Where(x => x.MenuPlanVersionId == day.ResolvedMenuPlanVersionId).OrderBy(x => x.DayNumber).ToListAsync(ct);
        if (menuDays.Count == 0) return [];
        var version = await _db.MenuPlanVersions.AsNoTracking().FirstAsync(x => x.Id == day.ResolvedMenuPlanVersionId, ct);
        var menuDay = menuDays[Math.Abs(day.ServiceDate.DayNumber - version.EffectiveFrom.DayNumber) % menuDays.Count];
        var userId = _currentUser.UserId ?? Guid.Empty;
        var blockedCodes = await _db.CustomerAllergyPreferences.AsNoTracking().Where(x => x.CustomerUserId == userId).Select(x => x.AllergenCode.ToUpper()).ToListAsync(ct);
        var rows = await (from link in _db.MenuDayMeals.AsNoTracking()
                          join meal in _db.Meals.AsNoTracking() on link.MealId equals meal.Id
                          where link.MenuDayId == menuDay.Id && link.ProgramId == programId && !link.IsDeleted
                             && meal.ApprovalStatus == VerificationStatus.Approved && meal.IsAvailableForNewOrders
                          orderby link.Slot, link.SortOrder
                          select new { meal.Id, meal.NameAr, meal.NameEn, link.Slot, link.SortOrder, meal.PresentAllergenCodes, meal.MayContainAllergenCodes }).ToListAsync(ct);
        var ingredientPairs = await _db.MealIngredients.AsNoTracking().Where(x => rows.Select(m => m.Id).Contains(x.MealId))
            .Select(x => new { x.MealId, x.IngredientId }).ToListAsync(ct);
        return rows.Select(x => new AvailableDayMealDto(x.Id, menuDay.Id, x.NameAr, x.NameEn, x.Slot, x.SortOrder,
            HasBlockedAllergen(x.PresentAllergenCodes, blockedCodes) || HasBlockedAllergen(x.MayContainAllergenCodes, blockedCodes)
                ? "allergy_or_preference_conflict"
                : ingredientPairs.Any(pair => pair.MealId == x.Id && blockedCodes.Contains($"INGREDIENT:{pair.IngredientId}")) ? "ingredient_preference_conflict" : null)).ToList();
    }

    private static bool HasBlockedAllergen(string? codes, IReadOnlyCollection<string> blocked)
        => !string.IsNullOrWhiteSpace(codes) && codes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(x => blocked.Contains(x.ToUpperInvariant()));
}

public sealed record CheckoutCatalogItem(Guid ProgramId, string ProgramNameAr, string ProgramNameEn, Guid BundleId, string BundleNameAr, string BundleNameEn);
public sealed record CheckoutAvailabilityRequest(Guid ProgramId, Guid BundleId, RestaurantTier Tier, int DurationDays, DateOnly StartDate, Guid RegionId, bool IncludeFriday = false);
public sealed record StartCheckoutRequest(Guid RestaurantId, Guid ProgramId, Guid BundleId, RestaurantTier Tier, int DurationDays, DateOnly StartDate, Guid RegionId, string? DeliveryAddress, string CallbackUrl, string ErrorUrl, bool IncludeFriday = false, string? CorrelationId = null);
public sealed record EligibleRestaurantDto(Guid RestaurantId, string NameAr, string NameEn, Guid MenuPlanVersionId, decimal FinalPrice);
public sealed record CheckoutStartedDto(Guid SubscriptionId, string PaymentUrl, DateTime HoldExpiresAtUtc, Guid MenuPlanVersionId, decimal FinalPrice);
public sealed record SetDayRestaurantRequest(Guid RestaurantId);
public sealed record SetDayMealsRequest(IReadOnlyList<Guid> MealIds);
public sealed record AvailableDayMealDto(Guid MealId, Guid MenuDayId, string NameAr, string NameEn, string Slot, int SortOrder, string? BlockReason);
