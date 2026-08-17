using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCheckout;

public static class MealPreferenceFilter
{
    public const string AllergyConflict = "allergy_conflict";
    public const string IngredientPreferenceConflict = "ingredient_preference_conflict";
    public const string DietaryPreferenceConflict = "dietary_preference_conflict";

    public sealed record NamedIngredient(Guid Id, string NameAr, string NameEn, IReadOnlyList<string> AllergenCodes);
    public sealed record SavedChoice(string Code, bool IsDislike, string? NameAr, string? NameEn);
    public sealed record CustomerPreferenceState(
        IReadOnlySet<string> BlockedAllergens,
        IReadOnlySet<string> DislikedNameKeys,
        IReadOnlyList<string> DietaryCodes);

    public static CustomerPreferenceState Empty { get; } = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        []);

    public static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToUpperInvariant();
    }

    public static string IngredientGroupKey(string? nameAr, string? nameEn)
        => $"{NormalizeName(nameAr)}|{NormalizeName(nameEn)}";

    public static IReadOnlySet<string> NameKeys(string? nameAr, string? nameEn)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ar = NormalizeName(nameAr);
        var en = NormalizeName(nameEn);
        if (ar.Length > 0) keys.Add("AR:" + ar);
        if (en.Length > 0) keys.Add("EN:" + en);
        return keys;
    }

    public static CustomerPreferenceState CreateState(
        IEnumerable<SavedChoice> choices,
        IEnumerable<NamedIngredient> resolvedDislikedIngredients,
        IReadOnlyList<string> dietaryCodes)
    {
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dislikedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var choice in choices)
        {
            if (choice.IsDislike)
            {
                foreach (var key in NameKeys(choice.NameAr, choice.NameEn))
                    dislikedKeys.Add(key);
                continue;
            }

            var code = FoodCodeNormalizer.Normalize(choice.Code);
            if (code.Length > 0) blocked.Add(code);
        }

        foreach (var ingredient in resolvedDislikedIngredients)
        {
            foreach (var key in NameKeys(ingredient.NameAr, ingredient.NameEn))
                dislikedKeys.Add(key);
        }

        return new CustomerPreferenceState(blocked, dislikedKeys, dietaryCodes);
    }

    public static async Task<CustomerPreferenceState> LoadAsync(IAppDbContext db, Guid? customerUserId, CancellationToken ct)
    {
        if (customerUserId is null) return Empty;
        var rows = await db.CustomerAllergyPreferences.AsNoTracking()
            .Where(x => x.CustomerUserId == customerUserId.Value)
            .ToListAsync(ct);
        var choices = rows.Select(x => new SavedChoice(x.AllergenCode, x.IsDislike, x.AllergenNameAr, x.AllergenNameEn)).ToList();
        var dietary = await db.CustomerDietaryPreferences.AsNoTracking()
            .Where(x => x.CustomerUserId == customerUserId.Value)
            .Select(x => x.PreferenceCode)
            .ToListAsync(ct);
        var dislikeIds = choices
            .Where(x => x.IsDislike && x.Code.StartsWith("INGREDIENT:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(x.Code[11..], out _))
            .Select(x => Guid.Parse(x.Code[11..]))
            .Distinct()
            .ToList();
        var ingredients = dislikeIds.Count == 0
            ? []
            : (await db.Ingredients.AsNoTracking()
                .Where(x => dislikeIds.Contains(x.Id))
                .Select(x => new { x.Id, x.NameAr, x.NameEn, x.AllergenCodes })
                .ToListAsync(ct))
            .Select(x => new NamedIngredient(x.Id, x.NameAr, x.NameEn, FoodCodeNormalizer.Split(x.AllergenCodes)))
            .ToList();
        return CreateState(choices, ingredients, dietary);
    }

    public static IReadOnlyList<string> DeriveDietaryTags(
        string? nutritionTagCodes,
        IEnumerable<string> mealPreferenceCodes,
        IReadOnlyList<NamedIngredient> ingredients,
        IReadOnlyList<(Guid IngredientId, string PreferenceCode)> ingredientPreferences)
    {
        var derived = ingredientPreferences
            .Where(preference => ingredients.Count > 0
                && ingredients.All(ingredient => ingredientPreferences.Any(x => x.IngredientId == ingredient.Id && x.PreferenceCode == preference.PreferenceCode)))
            .Select(x => x.PreferenceCode);
        return FoodCodeNormalizer.Split(nutritionTagCodes)
            .Concat(mealPreferenceCodes)
            .Concat(derived)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? BlockReason(
        IEnumerable<string> presentAllergens,
        IEnumerable<string> mayContainAllergens,
        IReadOnlyList<NamedIngredient> ingredients,
        IReadOnlyList<string> dietaryTags,
        CustomerPreferenceState prefs)
    {
        var effective = presentAllergens
            .Concat(mayContainAllergens)
            .Concat(ingredients.SelectMany(x => x.AllergenCodes))
            .Select(FoodCodeNormalizer.Normalize)
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (effective.Overlaps(prefs.BlockedAllergens)) return AllergyConflict;
        if (ingredients.Any(x => NameKeys(x.NameAr, x.NameEn).Overlaps(prefs.DislikedNameKeys))) return IngredientPreferenceConflict;
        if (!prefs.DietaryCodes.All(code => dietaryTags.Contains(code, StringComparer.OrdinalIgnoreCase))) return DietaryPreferenceConflict;
        return null;
    }

    public static string? BlockReason(MealSnapshotItem meal, CustomerPreferenceState prefs)
        => BlockReason(
            meal.PresentAllergenCodes,
            meal.MayContainAllergenCodes,
            meal.Ingredients.Select(x => new NamedIngredient(x.Id, x.NameAr, x.NameEn, x.AllergenCodes)).ToList(),
            meal.DietaryPreferenceCodes,
            prefs);
}
