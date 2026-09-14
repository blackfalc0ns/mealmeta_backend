using FluentAssertions;
using MealMate.Application.Features.CalendarMeals;
using MealMate.Application.Features.CustomerCheckout;
using Xunit;

namespace MealMate.UnitTests.CalendarMeals;

public sealed class MealFitnessScorerTests
{
    private readonly DateOnly _targetDate = new(2026, 9, 15);

    private static MealSnapshotItem CreateMeal(
        Guid id,
        string name,
        string slot,
        decimal kcal,
        IReadOnlyList<string> presentAllergens,
        IReadOnlyList<MealSnapshotIngredient> ingredients,
        IReadOnlyList<string>? dietaryCodes = null)
    {
        return new MealSnapshotItem(
            MealId: id,
            NameAr: name,
            NameEn: name,
            DescriptionAr: null,
            DescriptionEn: null,
            Slot: slot,
            SortOrder: 1,
            ImageUrl: null,
            Calories: kcal,
            ProteinG: 30,
            CarbsG: 50,
            FatG: 15,
            PresentAllergenCodes: presentAllergens,
            MayContainAllergenCodes: [],
            DietaryPreferenceCodes: dietaryCodes ?? [],
            Ingredients: ingredients);
    }

    [Fact]
    public void ComputeMealScore_ReturnsNull_WhenAllergenPresent()
    {
        var prefs = new MealPreferenceFilter.CustomerPreferenceState(
            BlockedAllergens: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PEANUTS" },
            DislikedNameKeys: new HashSet<string>(),
            DietaryCodes: []);

        var meal = CreateMeal(Guid.NewGuid(), "Peanut Butter Oats", "Breakfast", 350, ["PEANUTS"], []);

        var score = MealFitnessScorer.ComputeMealScore(meal, _targetDate, prefs, history: [], restaurantRating: 4.5m);

        score.Should().BeNull();
    }

    [Fact]
    public void ComputeMealScore_AppliesHeavyRecencyDecay_WhenConsumedYesterday()
    {
        var mealId = Guid.NewGuid();
        var prefs = MealPreferenceFilter.Empty;

        var meal = CreateMeal(mealId, "Grilled Chicken Rice", "Lunch", 500, [], []);

        var historyYesterday = new List<ConsumptionHistoryItem>
        {
            new(_targetDate.AddDays(-1), Guid.NewGuid(), mealId, "Lunch", WasManuallySelected: false)
        };

        var historyOlder = new List<ConsumptionHistoryItem>
        {
            new(_targetDate.AddDays(-8), Guid.NewGuid(), mealId, "Lunch", WasManuallySelected: false)
        };

        var scoreYesterday = MealFitnessScorer.ComputeMealScore(meal, _targetDate, prefs, historyYesterday, restaurantRating: 4.0m);
        var scoreOlder = MealFitnessScorer.ComputeMealScore(meal, _targetDate, prefs, historyOlder, restaurantRating: 4.0m);

        scoreYesterday.Should().NotBeNull();
        scoreOlder.Should().NotBeNull();
        (scoreOlder!.Value - scoreYesterday!.Value).Should().BeGreaterOrEqualTo(100);
    }

    [Fact]
    public void ComputeMealScore_PenalizesDislikedIngredients()
    {
        var prefsClean = MealPreferenceFilter.Empty;
        var prefsDislike = new MealPreferenceFilter.CustomerPreferenceState(
            BlockedAllergens: new HashSet<string>(),
            DislikedNameKeys: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EN:MUSHROOM" },
            DietaryCodes: []);

        var meal = CreateMeal(
            Guid.NewGuid(), "Mushroom Risotto", "Dinner", 450, [],
            [new MealSnapshotIngredient(Guid.NewGuid(), "مشروم", "Mushroom", null, null, false, true, [])]);

        var scoreClean = MealFitnessScorer.ComputeMealScore(meal, _targetDate, prefsClean, history: [], restaurantRating: 4.0m);
        var scoreDislike = MealFitnessScorer.ComputeMealScore(meal, _targetDate, prefsDislike, history: [], restaurantRating: 4.0m);

        (scoreClean!.Value - scoreDislike!.Value).Should().BeGreaterOrEqualTo(70);
    }
}
