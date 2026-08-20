namespace MealMate.Application.Features.CalendarMeals;

public sealed record CandidateMealOption(
    Guid MealId,
    string MealName,
    string Slot,
    IReadOnlyList<string> Ingredients,
    IReadOnlyList<string> Allergens);

public interface ISmartAutoSelectionService
{
    IReadOnlyList<CandidateMealOption> FilterSafeMeals(
        IEnumerable<CandidateMealOption> candidateMeals,
        ISet<string> customerAllergenNames);

    IReadOnlyList<CandidateMealOption> AutoSelectMealsForDay(
        IEnumerable<CandidateMealOption> candidateMeals,
        ISet<string> customerAllergenNames,
        IReadOnlyList<string> requiredSlots);
}

public sealed class SmartAutoSelectionService : ISmartAutoSelectionService
{
    public IReadOnlyList<CandidateMealOption> FilterSafeMeals(
        IEnumerable<CandidateMealOption> candidateMeals,
        ISet<string> customerAllergenNames)
    {
        if (customerAllergenNames is null || customerAllergenNames.Count == 0)
        {
            return candidateMeals.ToList();
        }

        var safeMeals = new List<CandidateMealOption>();
        foreach (var meal in candidateMeals)
        {
            var hasAllergen = meal.Allergens.Any(a => customerAllergenNames.Contains(a))
                              || meal.Ingredients.Any(i => customerAllergenNames.Contains(i));

            if (!hasAllergen)
            {
                safeMeals.Add(meal);
            }
        }

        return safeMeals;
    }

    public IReadOnlyList<CandidateMealOption> AutoSelectMealsForDay(
        IEnumerable<CandidateMealOption> candidateMeals,
        ISet<string> customerAllergenNames,
        IReadOnlyList<string> requiredSlots)
    {
        var safe = FilterSafeMeals(candidateMeals, customerAllergenNames);
        var result = new List<CandidateMealOption>();

        foreach (var slot in requiredSlots)
        {
            var matched = safe.FirstOrDefault(x => string.Equals(x.Slot, slot, StringComparison.OrdinalIgnoreCase));
            if (matched is not null)
            {
                result.Add(matched);
            }
        }

        return result;
    }
}
