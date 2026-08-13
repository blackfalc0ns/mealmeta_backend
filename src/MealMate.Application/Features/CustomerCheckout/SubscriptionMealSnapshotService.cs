using System.Text.Json;
using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCheckout;

public sealed record MealSnapshotIngredient(Guid Id, string NameAr, string NameEn, string? OriginCountryCode, string? SupplierName, bool IsLocal, bool IsHalalCertified, IReadOnlyList<string> AllergenCodes, bool IsOriginVisibleToCustomers = true);
public sealed record MealSnapshotItem(Guid MealId, string NameAr, string NameEn, string? DescriptionAr, string? DescriptionEn, string Slot, int SortOrder, string? ImageUrl, decimal? Calories, decimal? ProteinG, decimal? CarbsG, decimal? FatG, IReadOnlyList<string> PresentAllergenCodes, IReadOnlyList<string> MayContainAllergenCodes, IReadOnlyList<string> DietaryPreferenceCodes, IReadOnlyList<MealSnapshotIngredient> Ingredients);

public interface ISubscriptionMealSnapshotService
{
    Task<int> CreateMissingAsync(Guid subscriptionId, CancellationToken cancellationToken = default);
}

public sealed class SubscriptionMealSnapshotService : ISubscriptionMealSnapshotService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    public SubscriptionMealSnapshotService(IAppDbContext db, IDateTimeProvider clock) => (_db, _clock) = (db, clock);

    public async Task<int> CreateMissingAsync(Guid subscriptionId, CancellationToken ct = default)
    {
        var subscription = await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == subscriptionId, ct);
        if (subscription is null) return 0;
        var existingDayIds = await _db.SubscriptionDayMealSnapshots.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId).Select(x => x.SubscriptionDayId).ToListAsync(ct);
        var days = await _db.SubscriptionDays.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId && x.RestaurantId != null && x.ResolvedMenuPlanVersionId != null && !existingDayIds.Contains(x.Id)).ToListAsync(ct);
        foreach (var day in days)
        {
            var version = await _db.MenuPlanVersions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == day.ResolvedMenuPlanVersionId, ct);
            if (version is null) continue;
            var mode = await _db.MenuPlans.AsNoTracking().Where(x => x.Id == version.MenuPlanId).Select(x => (ScheduleMode?)x.Mode).FirstOrDefaultAsync(ct);
            if (mode is null) continue;
            var assignments = await ResolveAssignmentsAsync(version.Id, version.EffectiveFrom, mode.Value, day.ServiceDate, subscription.ProgramId, ct);
            var requiredSlots = await _db.ProgramMealSlotRequirements.AsNoTracking().Where(x => x.ProgramId == subscription.ProgramId && x.IsRequired).OrderBy(x => x.Slot).Select(x => x.Slot).ToListAsync(ct);
            if (requiredSlots.Count == 0 || assignments.Count == 0 || requiredSlots.Any(slot => assignments.All(x => !string.Equals(x.Slot, slot, StringComparison.OrdinalIgnoreCase))))
                continue;
            var mealIds = assignments.Select(x => x.MealId).Distinct().ToList();
            var meals = await _db.Meals.AsNoTracking().Where(x => mealIds.Contains(x.Id)).ToListAsync(ct);
            var images = await _db.MealImages.AsNoTracking().Where(x => mealIds.Contains(x.MealId)).OrderByDescending(x => x.IsPrimary).ThenBy(x => x.DisplayOrder).ToListAsync(ct);
            var ingredientLinks = await _db.MealIngredients.AsNoTracking().Where(x => mealIds.Contains(x.MealId))
                .Join(_db.Ingredients.AsNoTracking(), x => x.IngredientId, x => x.Id, (link, ingredient) => new { link.MealId, ingredient }).ToListAsync(ct);
            var preferenceLinks = await _db.MealFoodPreferences.AsNoTracking().Where(x => mealIds.Contains(x.MealId)).ToListAsync(ct);
            var ingredientIds = ingredientLinks.Select(x => x.ingredient.Id).Distinct().ToList();
            var ingredientPreferences = await _db.IngredientFoodPreferences.AsNoTracking().Where(x => ingredientIds.Contains(x.IngredientId)).ToListAsync(ct);
            var payload = assignments.Where(a => meals.Any(x => x.Id == a.MealId)).Select(a =>
            {
                var meal = meals.Single(x => x.Id == a.MealId);
                var ingredients = ingredientLinks.Where(x => x.MealId == meal.Id).Select(x => new MealSnapshotIngredient(x.ingredient.Id, x.ingredient.NameAr, x.ingredient.NameEn, x.ingredient.OriginCountryCode, x.ingredient.SupplierName, x.ingredient.IsLocal, x.ingredient.IsHalalCertified, FoodCodeNormalizer.Split(x.ingredient.AllergenCodes), x.ingredient.IsOriginVisibleToCustomers)).ToList();
                var ingredientDerivedPreferences = ingredientPreferences
                    .Where(x => ingredients.Count > 0 && ingredients.All(i => ingredientPreferences.Any(p => p.IngredientId == i.Id && p.PreferenceCode == x.PreferenceCode)))
                    .Select(x => x.PreferenceCode);
                return new MealSnapshotItem(meal.Id, meal.NameAr, meal.NameEn, meal.DescriptionAr, meal.DescriptionEn, a.Slot, a.SortOrder,
                    images.FirstOrDefault(x => x.MealId == meal.Id)?.ImageUrl, meal.Calories, meal.ProteinG, meal.CarbsG, meal.FatG,
                    FoodCodeNormalizer.Split(meal.PresentAllergenCodes), FoodCodeNormalizer.Split(meal.MayContainAllergenCodes),
                    FoodCodeNormalizer.Split(meal.NutritionTagCodes)
                        .Concat(preferenceLinks.Where(x => x.MealId == meal.Id).Select(x => x.PreferenceCode))
                        .Concat(ingredientDerivedPreferences)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList(), ingredients);
            }).ToList();
            if (payload.Count == 0) continue;
            _db.SubscriptionDayMealSnapshots.Add(new SubscriptionDayMealSnapshot
            {
                SubscriptionId = subscriptionId, SubscriptionDayId = day.Id, ServiceDate = day.ServiceDate,
                RestaurantId = day.RestaurantId!.Value, ProgramId = subscription.ProgramId, MenuPlanVersionId = version.Id,
                RequiredSlotsJson = JsonSerializer.Serialize(requiredSlots), MealsJson = JsonSerializer.Serialize(payload), CapturedAtUtc = _clock.UtcNow
            });
        }
        var created = _db.SubscriptionDayMealSnapshots.Local.Count(x => x.SubscriptionId == subscriptionId && !existingDayIds.Contains(x.SubscriptionDayId));
        if (created > 0) await _db.SaveChangesAsync(ct);
        return created;
    }

    private async Task<List<(Guid MealId, string Slot, int SortOrder)>> ResolveAssignmentsAsync(Guid versionId, DateOnly effectiveFrom, ScheduleMode mode, DateOnly date, Guid programId, CancellationToken ct)
    {
        if (mode == ScheduleMode.Weekday)
        {
            var rows = await _db.WeekdayMealAssignments.AsNoTracking().Where(x => x.MenuPlanVersionId == versionId && x.DayOfWeek == date.DayOfWeek && x.ProgramId == programId).OrderBy(x => x.Slot).ThenBy(x => x.SortOrder).Select(x => new { x.MealId, x.Slot, x.SortOrder }).ToListAsync(ct);
            return rows.Select(x => (x.MealId, x.Slot, x.SortOrder)).ToList();
        }
        var days = await _db.MenuDays.AsNoTracking().Where(x => x.MenuPlanVersionId == versionId).OrderBy(x => x.DayNumber).Select(x => x.Id).ToListAsync(ct);
        if (days.Count == 0) return [];
        var dayId = days[Math.Abs(date.DayNumber - effectiveFrom.DayNumber) % days.Count];
        var cycleRows = await _db.MenuDayMeals.AsNoTracking().Where(x => x.MenuDayId == dayId && x.ProgramId == programId).OrderBy(x => x.Slot).ThenBy(x => x.SortOrder).Select(x => new { x.MealId, x.Slot, x.SortOrder }).ToListAsync(ct);
        return cycleRows.Select(x => (x.MealId, x.Slot, x.SortOrder)).ToList();
    }
}
