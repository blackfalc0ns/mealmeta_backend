using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramNutrition;
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

    public RestaurantEligibilityService(IAppDbContext db, IDateTimeProvider clock) => (_db, _clock) = (db, clock);

    public async Task<IReadOnlyList<RestaurantEligibilityDto>> EvaluateAsync(RestaurantEligibilityRequest request, CancellationToken ct = default)
    {
        var dates = ServiceDates(request.StartDate, request.DurationDays, request.IncludeFriday).ToList();
        var programActive = await _db.Programs.AsNoTracking().AnyAsync(x => x.Id == request.ProgramId && x.IsActive && x.Status == ProgramStatus.Active, ct);
        var bundleActive = await _db.Bundles.AsNoTracking().AnyAsync(x => x.Id == request.BundleId && x.ProgramId == request.ProgramId && x.IsActive && x.Status == ProgramStatus.Active, ct);
        var restaurants = await _db.RestaurantProfiles.AsNoTracking()
            .Where(x => (!request.RestaurantId.HasValue || x.Id == request.RestaurantId) && x.VerificationStatus == VerificationStatus.Approved && x.IsOperationallyActive)
            .Select(x => new { x.Id, x.TradeNameAr, x.TradeNameEn }).ToListAsync(ct);
        var result = new List<RestaurantEligibilityDto>(restaurants.Count);

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

        foreach (var restaurant in restaurants)
        {
            var reasons = new HashSet<string>(StringComparer.Ordinal);
            if (!programActive || !bundleActive) reasons.Add(EligibilityReasonCodes.ProgramInactive);
            if (!await _db.RestaurantProgramBundleOffers.AsNoTracking().AnyAsync(x => x.RestaurantId == restaurant.Id && x.ProgramId == request.ProgramId && x.BundleId == request.BundleId && x.IsActive, ct))
                reasons.Add(EligibilityReasonCodes.OfferingMissing);
            if (!await _db.RestaurantServiceRegions.AsNoTracking().AnyAsync(x => x.RestaurantId == restaurant.Id && x.RegionId == request.RegionId && x.IsActive, ct))
                reasons.Add(EligibilityReasonCodes.RegionNotCovered);
            var priceValid = await _db.RestaurantBundlePricings.AsNoTracking().AnyAsync(x => x.RestaurantId == restaurant.Id && x.ProgramId == request.ProgramId && x.BundleId == request.BundleId
                && x.EffectiveFromUtc <= _clock.UtcNow && (x.EffectiveToUtc == null || x.EffectiveToUtc > _clock.UtcNow)
                && (x.LockStatus == PriceLockStatus.Locked || x.LockStatus == PriceLockStatus.Approved || x.LockStatus == PriceLockStatus.ApprovedWithEffectiveDate)
                && x.ReferencePrice26.Amount > 0 && x.ContractedSupplyPrice26.Amount > 0, ct);
            if (!priceValid || !await _db.PricingRuleVersions.AsNoTracking().AnyAsync(x => x.IsActive, ct)) reasons.Add(EligibilityReasonCodes.PricingUnavailable);
            var tier = await (from c in _db.RestaurantClassifications.AsNoTracking()
                              join run in _db.ClassificationRuns.AsNoTracking() on c.ClassificationRunId equals run.Id
                              where c.RestaurantId == restaurant.Id && c.ProgramId == request.ProgramId && c.BundleId == request.BundleId
                              orderby run.RunAtUtc descending
                              select (RestaurantTier?)c.Tier).FirstOrDefaultAsync(ct);
            if (tier != request.Tier) reasons.Add(EligibilityReasonCodes.TierMismatch);

            var versions = new List<EligibleMenuDateDto>();
            foreach (var date in dates)
            {
                var exception = await _db.RestaurantOperatingDayExceptions.AsNoTracking().FirstOrDefaultAsync(x => x.RestaurantId == restaurant.Id && x.Date == date, ct);
                var schedulesExist = await _db.RestaurantOperatingSchedules.AsNoTracking().AnyAsync(x => x.RestaurantId == restaurant.Id, ct);
                var working = exception?.IsWorkingDay ?? (!schedulesExist || await _db.RestaurantOperatingSchedules.AsNoTracking().AnyAsync(x => x.RestaurantId == restaurant.Id && x.DayOfWeek == date.DayOfWeek && x.IsWorkingDay, ct));
                if (!working) reasons.Add(EligibilityReasonCodes.RestaurantClosed);
                var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                var dayEnd = dayStart.AddDays(1);
                if (await _db.BusyStatuses.AsNoTracking().AnyAsync(x => x.RestaurantId == restaurant.Id && x.IsBusy && x.StartsAtUtc < dayEnd && (x.EndsAtUtc == null || x.EndsAtUtc >= dayStart), ct))
                    reasons.Add(EligibilityReasonCodes.RestaurantBusy);

                var version = await (from v in _db.MenuPlanVersions.AsNoTracking()
                                     join plan in _db.MenuPlans.AsNoTracking() on v.MenuPlanId equals plan.Id
                                     join pp in _db.MenuPlanPrograms.AsNoTracking() on plan.Id equals pp.MenuPlanId
                                     where plan.RestaurantId == restaurant.Id && pp.ProgramId == request.ProgramId
                                        && pp.EffectiveFrom <= date && (pp.EffectiveTo == null || pp.EffectiveTo >= date)
                                        && v.Status == VerificationStatus.Approved && !v.IsPaused && v.EffectiveFrom <= date
                                     orderby v.EffectiveFrom descending, v.VersionNumber descending
                                     select new { Version = v, plan.Mode }).FirstOrDefaultAsync(ct);
                if (version is null) { reasons.Add(EligibilityReasonCodes.MenuPlanMissing); continue; }
                versions.Add(new EligibleMenuDateDto(date, version.Version.Id));
                var requiredSlots = await _db.ProgramMealSlotRequirements.AsNoTracking().Where(x => x.ProgramId == request.ProgramId && x.IsRequired).Select(x => x.Slot.ToUpper()).Distinct().ToListAsync(ct);
                if (requiredSlots.Count == 0) { reasons.Add(EligibilityReasonCodes.MenuSlotIncomplete); continue; }
                var availableMealIdsBySlot = await ResolveMealsAsync(version.Version.Id, version.Version.EffectiveFrom, version.Mode, date, request.ProgramId, ct);
                foreach (var requiredSlot in requiredSlots)
                {
                    if (!availableMealIdsBySlot.TryGetValue(requiredSlot, out var mealIds) || mealIds.Count == 0) { reasons.Add(EligibilityReasonCodes.MenuSlotIncomplete); continue; }
                    if (!await HasSafeMealAsync(mealIds, blockedAllergens, dislikedIds, dietaryCodes, restaurant.Id, ct)) reasons.Add(EligibilityReasonCodes.NoSafeMeals);
                }

                var limit = await _db.DailyCapacities.AsNoTracking().Where(x => x.RestaurantId == restaurant.Id && x.ServiceDate == date && x.CapacityType == CapacityType.DateOverride).Select(x => (int?)x.MaxBoxes).FirstOrDefaultAsync(ct)
                    ?? await _db.DailyCapacities.AsNoTracking().Where(x => x.RestaurantId == restaurant.Id && x.CapacityType == CapacityType.Base && x.ServiceDate <= date).OrderByDescending(x => x.ServiceDate).Select(x => (int?)x.MaxBoxes).FirstOrDefaultAsync(ct);
                if (limit is null || limit <= 0) { reasons.Add(EligibilityReasonCodes.CapacityUnavailable); continue; }
                var confirmed = await _db.SubscriptionDays.AsNoTracking().CountAsync(x => x.RestaurantId == restaurant.Id && x.ServiceDate == date && x.Status != SubscriptionDayStatus.Cancelled, ct);
                var pending = await _db.CapacityHolds.AsNoTracking().Where(x => x.RestaurantId == restaurant.Id && x.ServiceDate == date && x.Status == CapacityHoldStatus.PendingPayment && x.ExpiresAtUtc > _clock.UtcNow).SumAsync(x => (int?)x.Quantity, ct) ?? 0;
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

    private async Task<bool> HasSafeMealAsync(IReadOnlyList<Guid> mealIds, HashSet<string> blockedAllergens, HashSet<Guid> dislikedIds, IReadOnlyList<string> dietaryCodes, Guid restaurantId, CancellationToken ct)
    {
        var meals = await _db.Meals.AsNoTracking().Where(x => mealIds.Contains(x.Id) && x.RestaurantId == restaurantId && x.ApprovalStatus == VerificationStatus.Approved && x.IsAvailableForNewOrders && x.AllergensReviewed && x.DataResponsibilityConfirmed)
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
            if (dietaryCodes.All(tags.Contains)) return true;
        }
        return false;
    }

    public static IEnumerable<DateOnly> ServiceDates(DateOnly start, int count, bool includeFriday)
    {
        for (var date = start; count > 0; date = date.AddDays(1))
            if (includeFriday || date.DayOfWeek != DayOfWeek.Friday) { yield return date; count--; }
    }
}
