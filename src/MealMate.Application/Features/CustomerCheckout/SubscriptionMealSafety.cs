using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramNutrition;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCheckout;

internal static class SubscriptionMealSafety
{
    internal static async Task<HashSet<Guid>> ResolveAsync(IAppDbContext db, IReadOnlyList<Guid> mealIds, Guid programId, MealPreferenceFilter.CustomerPreferenceState prefs, CancellationToken ct)
    {
        if (mealIds.Count == 0) return [];
        var meals = await db.Meals.AsNoTracking().Where(x => mealIds.Contains(x.Id) && x.ApprovalStatus == VerificationStatus.Approved && x.IsAvailableForNewOrders && x.AllergensReviewed && x.DataResponsibilityConfirmed)
            .Select(x => new { x.Id, x.ProgramId, x.Slot, x.Calories, x.ProteinG, x.CarbsG, x.NetCarbsG, x.FatG, x.FiberG, x.PresentAllergenCodes, x.MayContainAllergenCodes, x.NutritionTagCodes }).ToListAsync(ct);
        var links = await db.MealIngredients.AsNoTracking().Where(x => meals.Select(m => m.Id).Contains(x.MealId))
            .Join(db.Ingredients.AsNoTracking(), x => x.IngredientId, x => x.Id, (link, ingredient) => new { link.MealId, Ingredient = ingredient }).ToListAsync(ct);
        var mealPrefs = await db.MealFoodPreferences.AsNoTracking().Where(x => meals.Select(m => m.Id).Contains(x.MealId)).ToListAsync(ct);
        var ingredientIds = links.Select(x => x.Ingredient.Id).Distinct().ToList();
        var ingredientPrefs = (await db.IngredientFoodPreferences.AsNoTracking().Where(x => ingredientIds.Contains(x.IngredientId)).ToListAsync(ct))
            .Select(x => (x.IngredientId, x.PreferenceCode)).ToList();
        var assignedIds = (await db.MealProgramAssignments.AsNoTracking()
            .Where(x => mealIds.Contains(x.MealId) && x.ProgramId == programId)
            .Select(x => x.MealId).ToListAsync(ct)).ToHashSet();
        var programIds = new[] { programId };
        var standards = await db.ProgramNutritionStandards.AsNoTracking().Where(x => programIds.Contains(x.ProgramId) && x.IsActive).ToListAsync(ct);
        var programs = await db.Programs.AsNoTracking().Where(x => programIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var mealsWithImages = await db.MealImages.AsNoTracking().Where(x => mealIds.Contains(x.MealId)).Select(x => x.MealId).Distinct().ToListAsync(ct);
        var safe = new HashSet<Guid>();
        foreach (var meal in meals)
        {
            if ((meal.ProgramId != programId && !assignedIds.Contains(meal.Id)) || !mealsWithImages.Contains(meal.Id)) continue;
            var ingredients = links.Where(x => x.MealId == meal.Id).Select(x => x.Ingredient).ToList();
            if (ingredients.Count == 0 || ingredients.Any(x => !x.IsActive || string.IsNullOrWhiteSpace(x.OriginCountryCode))) continue;
            var standard = standards.FirstOrDefault(x => x.ProgramId == programId && x.MealCategory == NutritionMealCategories.FromSlot(meal.Slot));
            if (standard is null || MealNutritionStandardValidator.Evaluate(standard, programs.GetValueOrDefault(programId)?.ShortCode ?? programs.GetValueOrDefault(programId)?.Code,
                meal.Calories, meal.ProteinG, meal.CarbsG, meal.NetCarbsG, meal.FatG, meal.FiberG).Count > 0) continue;
            var named = ingredients.Select(x => new MealPreferenceFilter.NamedIngredient(x.Id, x.NameAr, x.NameEn, FoodCodeNormalizer.Split(x.AllergenCodes))).ToList();
            var tags = MealPreferenceFilter.DeriveDietaryTags(meal.NutritionTagCodes, mealPrefs.Where(x => x.MealId == meal.Id).Select(x => x.PreferenceCode), named, ingredientPrefs);
            if (MealPreferenceFilter.BlockReason(FoodCodeNormalizer.Split(meal.PresentAllergenCodes), FoodCodeNormalizer.Split(meal.MayContainAllergenCodes), named, tags, prefs) is null)
                safe.Add(meal.Id);
        }
        return safe;
    }

}
