namespace MealMate.Application.Features.FamilyCheckout;

/// <summary>UI control codes, never allergen or dietary definition rows.</summary>
public static class FamilyPreferenceSelectionCodes
{
    public const string NoAllergies = "NO_ALLERGIES";
    public const string NoDietaryRestrictions = "NO_DIETARY_RESTRICTIONS";

    public static bool IsNone(string? value, string noneCode)
        => !string.IsNullOrWhiteSpace(value) && (value.Trim().Equals(noneCode, StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase));

    public static bool HasConflictingNone(IEnumerable<string> values, string noneCode)
    {
        var codes = values.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        return codes.Any(x => IsNone(x, noneCode)) && codes.Any(x => !IsNone(x, noneCode));
    }

    public static IReadOnlyList<string> SelectedOptions(IEnumerable<string> values, bool completed, string noneCode)
    {
        var codes = values.ToList();
        return codes.Count == 0 && completed ? [noneCode] : codes;
    }
}
