using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramNutrition;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCheckout;

public static class EligibilityReasonCodes
{
    public const string ProgramInactive = "program_inactive";
    public const string OfferingMissing = "offering_missing";
    public const string PricingUnavailable = "pricing_unavailable";
    public const string TierMismatch = "tier_mismatch";
    public const string RegionNotCovered = "region_not_covered";
    public const string MenuPlanMissing = "menu_plan_missing";
    public const string MenuSlotIncomplete = "menu_slot_incomplete";
    public const string NoSafeMeals = "no_safe_meals";
    public const string RestaurantClosed = "restaurant_closed";
    public const string RestaurantBusy = "restaurant_busy";
    public const string CapacityUnavailable = "capacity_unavailable";
    public const string RestaurantNotApproved = "restaurant_not_approved";
    public const string RestaurantInactive = "restaurant_inactive";
}

public sealed record RestaurantEligibilityRequest(Guid ProgramId, Guid BundleId, RestaurantTier Tier, Guid RegionId, DateOnly StartDate, int DurationDays, bool IncludeFriday, Guid? CustomerUserId = null, Guid? RestaurantId = null);
public sealed record EligibleMenuDateDto(DateOnly ServiceDate, Guid MenuPlanVersionId);
public sealed record RestaurantEligibilityDto(Guid RestaurantId, string NameAr, string NameEn, bool IsEligible, IReadOnlyList<string> ReasonCodes, IReadOnlyList<EligibleMenuDateDto> MenuPlanVersionsByDate);

public interface IRestaurantEligibilityService
{
    Task<IReadOnlyList<RestaurantEligibilityDto>> EvaluateAsync(RestaurantEligibilityRequest request, CancellationToken cancellationToken = default);
}

public sealed class RestaurantEligibilityService : IRestaurantEligibilityService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ISubscriptionScheduleService _schedule;
    private readonly IBundleSlotRequirementService _slots;
    private readonly IMenuPlanResolutionService _menus;
    private readonly IRestaurantTierClassificationService _tiers;

    public RestaurantEligibilityService(IAppDbContext db, IDateTimeProvider clock, ISubscriptionScheduleService schedule, IBundleSlotRequirementService slots, IMenuPlanResolutionService menus,
        IRestaurantTierClassificationService tiers)
        => (_db, _clock, _schedule, _slots, _menus, _tiers) = (db, clock, schedule, slots, menus, tiers);

    public async Task<IReadOnlyList<RestaurantEligibilityDto>> EvaluateAsync(RestaurantEligibilityRequest request, CancellationToken ct = default)
    {
        var dates = _schedule.GetServiceDates(request.StartDate, request.DurationDays, request.IncludeFriday);
        var slotRequirements = await _slots.ResolveAsync(request.ProgramId, request.BundleId, ct);
        var programActive = await _db.Programs.AsNoTracking().AnyAsync(x => x.Id == request.ProgramId && x.IsActive && x.Status == ProgramStatus.Active, ct);
        var bundleActive = await _db.Bundles.AsNoTracking().AnyAsync(x => x.Id == request.BundleId && x.ProgramId == request.ProgramId && x.IsActive && x.Status == ProgramStatus.Active, ct);
        var restaurants = await _db.RestaurantProfiles.AsNoTracking()
            .Where(x => !request.RestaurantId.HasValue || x.Id == request.RestaurantId)
            .Select(x => new { x.Id, x.TradeNameAr, x.TradeNameEn, x.VerificationStatus, x.IsOperationallyActive }).ToListAsync(ct);
        var result = new List<RestaurantEligibilityDto>(restaurants.Count);
        var resolvedMenus = await _menus.ResolveManyAsync(restaurants.Select(x => x.Id).ToList(), request.ProgramId, dates, ct);

        var customerAllergyCodes = request.CustomerUserId.HasValue
            ? await _db.CustomerAllergyPreferences.AsNoTracking().Where(x => x.CustomerUserId == request.CustomerUserId && !x.IsDislike).Select(x => x.AllergenCode).ToListAsync(ct)
            : [];
        var dislikeCodes = request.CustomerUserId.HasValue
            ? await _db.CustomerAllergyPreferences.AsNoTracking().Where(x => x.CustomerUserId == request.CustomerUserId && x.IsDislike).Select(x => x.AllergenCode).ToListAsync(ct)
            : [];
        var dietaryCodes = request.CustomerUserId.HasValue
            ? await _db.CustomerDietaryPreferences.AsNoTracking().Where(x => x.CustomerUserId == request.CustomerUserId).Select(x => x.PreferenceCode).ToListAsync(ct)
            : [];
        var blockedAllergens = customerAllergyCodes.Select(FoodCodeNormalizer.Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dislikedIds = dislikeCodes.Where(x => x.StartsWith("INGREDIENT:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(x[11..], out _)).Select(x => Guid.Parse(x[11..])).ToHashSet();
        var restaurantIds = restaurants.Select(x => x.Id).ToList();
        var offers = (await _db.RestaurantProgramBundleOffers.AsNoTracking().Where(x => restaurantIds.Contains(x.RestaurantId) && x.ProgramId == request.ProgramId && x.BundleId == request.BundleId && x.IsActive).Select(x => x.RestaurantId).ToListAsync(ct)).ToHashSet();
        var coveredRegions = request.RegionId == Guid.Empty
            ? restaurantIds.ToHashSet()
            : (await _db.RestaurantServiceRegions.AsNoTracking().Where(x => restaurantIds.Contains(x.RestaurantId) && x.RegionId == request.RegionId && x.IsActive).Select(x => x.RestaurantId).ToListAsync(ct)).ToHashSet();
        var pricedRestaurants = (await _db.RestaurantBundlePricings.AsNoTracking().Where(x => restaurantIds.Contains(x.RestaurantId) && x.ProgramId == request.ProgramId && x.BundleId == request.BundleId
            && x.EffectiveFromUtc <= _clock.UtcNow && (x.EffectiveToUtc == null || x.EffectiveToUtc > _clock.UtcNow)
            && (x.LockStatus == PriceLockStatus.Locked || x.LockStatus == PriceLockStatus.Approved || x.LockStatus == PriceLockStatus.ApprovedWithEffectiveDate)
            && x.ReferencePrice26.Amount > 0 && x.ContractedSupplyPrice26.Amount > 0).Select(x => x.RestaurantId).ToListAsync(ct)).ToHashSet();
        var activePricingRule = await _db.PricingRuleVersions.AsNoTracking().AnyAsync(x => x.IsActive, ct);
        var classification = await _tiers.ResolveAsync(request.ProgramId, request.BundleId, eligibleOnly: true, ct);
        var tiers = classification.Assignments.ToDictionary(x => x.RestaurantId, x => x.Tier);
        var schedules = await _db.RestaurantOperatingSchedules.AsNoTracking().Where(x => restaurantIds.Contains(x.RestaurantId)).ToListAsync(ct);
        var exceptions = await _db.RestaurantOperatingDayExceptions.AsNoTracking().Where(x => restaurantIds.Contains(x.RestaurantId) && dates.Contains(x.Date)).ToListAsync(ct);
        var minDay = dates.Count > 0 ? dates.Min().ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : _clock.UtcNow;
        var maxDay = dates.Count > 0 ? dates.Max().AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : _clock.UtcNow;
        var busyStatuses = await _db.BusyStatuses.AsNoTracking().Where(x => restaurantIds.Contains(x.RestaurantId) && x.IsBusy && x.StartsAtUtc < maxDay && (x.EndsAtUtc == null || x.EndsAtUtc >= minDay)).ToListAsync(ct);
        var capacities = dates.Count == 0 ? [] : await _db.DailyCapacities.AsNoTracking().Where(x => restaurantIds.Contains(x.RestaurantId) && x.ServiceDate <= dates.Max()).ToListAsync(ct);
        var confirmedCounts = await _db.SubscriptionDays.AsNoTracking().Where(x => x.RestaurantId != null && restaurantIds.Contains(x.RestaurantId.Value) && dates.Contains(x.ServiceDate) && x.Status != SubscriptionDayStatus.Cancelled)
            .GroupBy(x => new { RestaurantId = x.RestaurantId!.Value, x.ServiceDate }).Select(x => new { x.Key.RestaurantId, x.Key.ServiceDate, Count = x.Count() }).ToListAsync(ct);
        var pendingCounts = await _db.CapacityHolds.AsNoTracking().Where(x => restaurantIds.Contains(x.RestaurantId) && dates.Contains(x.ServiceDate)
                && ((x.Status == CapacityHoldStatus.PendingPayment && x.ExpiresAtUtc > _clock.UtcNow) || x.Status == CapacityHoldStatus.PaymentConfirmed))
            .GroupBy(x => new { x.RestaurantId, x.ServiceDate }).Select(x => new { x.Key.RestaurantId, x.Key.ServiceDate, Count = x.Sum(v => v.Quantity) }).ToListAsync(ct);
        var allResolvedMealIds = resolvedMenus.Values.SelectMany(x => x.Assignments).Select(x => x.MealId).Distinct().ToList();
        var safeMealIds = await ResolveSafeMealIdsAsync(allResolvedMealIds, blockedAllergens, dislikedIds, dietaryCodes, ct);

        foreach (var restaurant in restaurants)
        {
            var reasons = new HashSet<string>(StringComparer.Ordinal);
            if (restaurant.VerificationStatus != VerificationStatus.Approved) reasons.Add(EligibilityReasonCodes.RestaurantNotApproved);
            if (!restaurant.IsOperationallyActive) reasons.Add(EligibilityReasonCodes.RestaurantInactive);
            if (!programActive || !bundleActive) reasons.Add(EligibilityReasonCodes.ProgramInactive);
            if (slotRequirements.Count == 0) reasons.Add(EligibilityReasonCodes.MenuSlotIncomplete);
            if (!offers.Contains(restaurant.Id))
                reasons.Add(EligibilityReasonCodes.OfferingMissing);
            if (!coveredRegions.Contains(restaurant.Id))
                reasons.Add(EligibilityReasonCodes.RegionNotCovered);
            if (!pricedRestaurants.Contains(restaurant.Id) || !activePricingRule) reasons.Add(EligibilityReasonCodes.PricingUnavailable);
            if (!tiers.TryGetValue(restaurant.Id, out var tier) || tier != request.Tier) reasons.Add(EligibilityReasonCodes.TierMismatch);

            var versions = new List<EligibleMenuDateDto>();
            foreach (var date in dates)
            {
                var exception = exceptions.FirstOrDefault(x => x.RestaurantId == restaurant.Id && x.Date == date);
                var restaurantSchedules = schedules.Where(x => x.RestaurantId == restaurant.Id).ToList();
                var working = exception?.IsWorkingDay
                    ?? (restaurantSchedules.Count == 0
                        ? date.DayOfWeek != DayOfWeek.Friday
                        : restaurantSchedules.Any(x => x.DayOfWeek == date.DayOfWeek && x.IsWorkingDay));
                if (!working) reasons.Add(EligibilityReasonCodes.RestaurantClosed);
                var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                var dayEnd = dayStart.AddDays(1);
                if (busyStatuses.Any(x => x.RestaurantId == restaurant.Id && x.StartsAtUtc < dayEnd && (x.EndsAtUtc == null || x.EndsAtUtc >= dayStart)))
                    reasons.Add(EligibilityReasonCodes.RestaurantBusy);

                var resolved = resolvedMenus.GetValueOrDefault((restaurant.Id, date));
                if (resolved is null) { reasons.Add(EligibilityReasonCodes.MenuPlanMissing); continue; }
                versions.Add(new EligibleMenuDateDto(date, resolved.MenuPlanVersionId));
                foreach (var requiredSlot in slotRequirements)
                {
                    var mealIds = resolved.Assignments.Where(x => string.Equals(x.Slot, requiredSlot.Slot, StringComparison.OrdinalIgnoreCase)).Select(x => x.MealId).Distinct().ToList();
                    if (mealIds.Count < requiredSlot.Count) { reasons.Add(EligibilityReasonCodes.MenuSlotIncomplete); continue; }
                    if (mealIds.Count(safeMealIds.Contains) < requiredSlot.Count) reasons.Add(EligibilityReasonCodes.NoSafeMeals);
                }

                var limit = capacities.Where(x => x.RestaurantId == restaurant.Id && x.ServiceDate == date && x.CapacityType == CapacityType.DateOverride).Select(x => (int?)x.MaxBoxes).FirstOrDefault()
                    ?? capacities.Where(x => x.RestaurantId == restaurant.Id && x.CapacityType == CapacityType.Base && x.ServiceDate <= date).OrderByDescending(x => x.ServiceDate).Select(x => (int?)x.MaxBoxes).FirstOrDefault();
                if (limit is null || limit <= 0) { reasons.Add(EligibilityReasonCodes.CapacityUnavailable); continue; }
                var confirmed = confirmedCounts.FirstOrDefault(x => x.RestaurantId == restaurant.Id && x.ServiceDate == date)?.Count ?? 0;
                var pending = pendingCounts.FirstOrDefault(x => x.RestaurantId == restaurant.Id && x.ServiceDate == date)?.Count ?? 0;
                if (confirmed + pending >= limit) reasons.Add(EligibilityReasonCodes.CapacityUnavailable);
            }
            result.Add(new RestaurantEligibilityDto(restaurant.Id, restaurant.TradeNameAr, restaurant.TradeNameEn, reasons.Count == 0 && versions.Count == dates.Count, reasons.OrderBy(x => x).ToList(), versions));
        }
        return result;
    }

    private async Task<Dictionary<string, List<Guid>>> ResolveMealsAsync(Guid versionId, DateOnly effectiveFrom, ScheduleMode mode, DateOnly date, Guid programId, CancellationToken ct)
    {
        if (mode == ScheduleMode.Weekday)
        {
            var rows = await _db.WeekdayMealAssignments.AsNoTracking().Where(x => x.MenuPlanVersionId == versionId && x.ProgramId == programId && x.DayOfWeek == date.DayOfWeek)
                .Select(x => new { x.Slot, x.MealId }).ToListAsync(ct);
            return rows.GroupBy(x => x.Slot.ToUpperInvariant()).ToDictionary(x => x.Key, x => x.Select(v => v.MealId).Distinct().ToList());
        }
        var days = await _db.MenuDays.AsNoTracking().Where(x => x.MenuPlanVersionId == versionId).OrderBy(x => x.DayNumber).Select(x => x.Id).ToListAsync(ct);
        if (days.Count == 0) return [];
        var dayId = days[Math.Abs(date.DayNumber - effectiveFrom.DayNumber) % days.Count];
        var cycleRows = await _db.MenuDayMeals.AsNoTracking().Where(x => x.MenuDayId == dayId && x.ProgramId == programId).Select(x => new { x.Slot, x.MealId }).ToListAsync(ct);
        return cycleRows.GroupBy(x => x.Slot.ToUpperInvariant()).ToDictionary(x => x.Key, x => x.Select(v => v.MealId).Distinct().ToList());
    }

    private async Task<HashSet<Guid>> ResolveSafeMealIdsAsync(IReadOnlyList<Guid> mealIds, HashSet<string> blockedAllergens, HashSet<Guid> dislikedIds, IReadOnlyList<string> dietaryCodes, CancellationToken ct)
    {
        if (mealIds.Count == 0) return [];
        var meals = await _db.Meals.AsNoTracking().Where(x => mealIds.Contains(x.Id) && x.ApprovalStatus == VerificationStatus.Approved && x.IsAvailableForNewOrders && x.AllergensReviewed && x.DataResponsibilityConfirmed)
            .Select(x => new { x.Id, x.ProgramId, x.Slot, x.Calories, x.ProteinG, x.CarbsG, x.NetCarbsG, x.FatG, x.FiberG, x.PresentAllergenCodes, x.MayContainAllergenCodes, x.NutritionTagCodes }).ToListAsync(ct);
        var links = await _db.MealIngredients.AsNoTracking().Where(x => meals.Select(m => m.Id).Contains(x.MealId))
            .Join(_db.Ingredients.AsNoTracking(), x => x.IngredientId, x => x.Id, (link, ingredient) => new { link.MealId, Ingredient = ingredient }).ToListAsync(ct);
        var mealPrefs = await _db.MealFoodPreferences.AsNoTracking().Where(x => meals.Select(m => m.Id).Contains(x.MealId)).ToListAsync(ct);
        var ingredientIds = links.Select(x => x.Ingredient.Id).Distinct().ToList();
        var ingredientPrefs = await _db.IngredientFoodPreferences.AsNoTracking().Where(x => ingredientIds.Contains(x.IngredientId)).ToListAsync(ct);
        var programIds = meals.Where(x => x.ProgramId.HasValue).Select(x => x.ProgramId!.Value).Distinct().ToList();
        var standards = await _db.ProgramNutritionStandards.AsNoTracking().Where(x => programIds.Contains(x.ProgramId) && x.IsActive).ToListAsync(ct);
        var programs = await _db.Programs.AsNoTracking().Where(x => programIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var mealsWithImages = await _db.MealImages.AsNoTracking().Where(x => mealIds.Contains(x.MealId)).Select(x => x.MealId).Distinct().ToListAsync(ct);
        var safe = new HashSet<Guid>();
        foreach (var meal in meals)
        {
            if (meal.ProgramId is not { } programId || !mealsWithImages.Contains(meal.Id)) continue;
            var ingredients = links.Where(x => x.MealId == meal.Id).Select(x => x.Ingredient).ToList();
            if (ingredients.Count == 0 || ingredients.Any(x => !x.IsActive || string.IsNullOrWhiteSpace(x.OriginCountryCode))) continue;
            var standard = standards.FirstOrDefault(x => x.ProgramId == programId && x.MealCategory == NutritionMealCategories.FromSlot(meal.Slot));
            if (standard is null || MealNutritionStandardValidator.Evaluate(standard, programs.GetValueOrDefault(programId)?.ShortCode ?? programs.GetValueOrDefault(programId)?.Code,
                meal.Calories, meal.ProteinG, meal.CarbsG, meal.NetCarbsG, meal.FatG, meal.FiberG).Count > 0) continue;
            var effectiveAllergens = FoodCodeNormalizer.Split(meal.PresentAllergenCodes).Concat(FoodCodeNormalizer.Split(meal.MayContainAllergenCodes)).Concat(ingredients.SelectMany(x => FoodCodeNormalizer.Split(x.AllergenCodes))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (effectiveAllergens.Overlaps(blockedAllergens) || ingredients.Any(x => dislikedIds.Contains(x.Id))) continue;
            var derivedIngredientTags = ingredientPrefs
                .Where(preference => ingredients.All(ingredient => ingredientPrefs.Any(x => x.IngredientId == ingredient.Id && x.PreferenceCode == preference.PreferenceCode)))
                .Select(x => x.PreferenceCode);
            var tags = FoodCodeNormalizer.Split(meal.NutritionTagCodes)
                .Concat(mealPrefs.Where(x => x.MealId == meal.Id).Select(x => x.PreferenceCode))
                .Concat(derivedIngredientTags)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (dietaryCodes.All(tags.Contains)) safe.Add(meal.Id);
        }
        return safe;
    }

}
