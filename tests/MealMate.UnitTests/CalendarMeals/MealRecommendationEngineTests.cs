using FluentAssertions;
using MealMate.Application.Features.CalendarMeals;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramNutrition;
using Xunit;

namespace MealMate.UnitTests.CalendarMeals;

public sealed class MealRecommendationEngineTests
{
    private readonly IMealRecommendationEngine _engine = new MealRecommendationEngine();
    private readonly DateOnly _targetDate = new(2026, 9, 15);

    private static MealSnapshotItem CreateMeal(Guid id, string name, string slot, decimal kcal)
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
            PresentAllergenCodes: [],
            MayContainAllergenCodes: [],
            DietaryPreferenceCodes: [],
            Ingredients: []);
    }

    [Fact]
    public async Task RecommendBoxForDayAsync_AvoidsYesterdayRestaurant_WhenAlternativeAvailable()
    {
        var restA = Guid.NewGuid();
        var restB = Guid.NewGuid();

        var slots = new List<SlotRequirementDto> { new("Lunch", 1) };
        var lunchA = CreateMeal(Guid.NewGuid(), "Burger A", "Lunch", 600);
        var lunchB = CreateMeal(Guid.NewGuid(), "Steak B", "Lunch", 600);

        var candidateA = new CandidateRestaurantMenu(restA, "Rest A", "Rest A", 4.5m, 10, false, false, 2, 5, [lunchA]);
        var candidateB = new CandidateRestaurantMenu(restB, "Rest B", "Rest B", 4.5m, 10, false, false, 1, 5, [lunchB]);

        var history = new List<ConsumptionHistoryItem>
        {
            new(_targetDate.AddDays(-1), restA, lunchA.MealId, "Lunch", WasManuallySelected: false)
        };

        var context = new MealRecommendationContext(
            Guid.NewGuid(), Guid.NewGuid(), _targetDate, CurrentRestaurantId: null,
            MealPreferenceFilter.Empty, null, slots, history, [candidateA, candidateB]);

        var result = await _engine.RecommendBoxForDayAsync(context);

        result.Should().NotBeNull();
        result!.RestaurantId.Should().Be(restB);
        result.SelectedMeals.Should().ContainSingle(x => x.MealId == lunchB.MealId);
    }

    [Fact]
    public async Task RecommendBoxForDayAsync_OptimizesForCalorieTarget()
    {
        var restId = Guid.NewGuid();
        var slots = new List<SlotRequirementDto> { new("Lunch", 1) };

        var mealFar = CreateMeal(Guid.NewGuid(), "Heavy Lunch", "Lunch", 950);
        var mealClose = CreateMeal(Guid.NewGuid(), "Balanced Lunch", "Lunch", 520);

        var candidate = new CandidateRestaurantMenu(restId, "Healthy Fit", "Healthy Fit", 4.8m, 50, false, false, 0, 10, [mealFar, mealClose]);

        var target = new ProgramDailyNutritionTargetDto(
            Guid.NewGuid(), "FIT", Guid.NewGuid(), "STD", 0, 1, 0, 0, 1,
            SuggestedKcal: 500, SuggestedProteinG: 40, SuggestedCarbsG: 45, SuggestedFatG: 12, SuggestedFiberG: 4,
            KcalMin: 450, KcalMax: 550, ProteinGMin: 35, ProteinGMax: 50, CarbsGMin: 30, CarbsGMax: 60, FatGMin: 10, FatGMax: 20,
            CarbMode: "Standard", KetoDayNetCarbsLimit: null, StandardVersion: "v1");

        var context = new MealRecommendationContext(
            Guid.NewGuid(), Guid.NewGuid(), _targetDate, CurrentRestaurantId: null,
            MealPreferenceFilter.Empty, target, slots, History: [], [candidate]);

        var result = await _engine.RecommendBoxForDayAsync(context);

        result.Should().NotBeNull();
        result!.SelectedMeals.Should().ContainSingle(x => x.MealId == mealClose.MealId);
    }

    [Fact]
    public async Task RecommendBoxForDayAsync_LocksPreselectedMeals()
    {
        var restId = Guid.NewGuid();
        var slots = new List<SlotRequirementDto> { new("Lunch", 1), new("Dinner", 1) };

        var lockedDinner = CreateMeal(Guid.NewGuid(), "Salmon Steak", "Dinner", 450);
        var otherDinner = CreateMeal(Guid.NewGuid(), "Beef Tenderloin", "Dinner", 550);
        var lunchChoice = CreateMeal(Guid.NewGuid(), "Chicken Breast", "Lunch", 400);

        var candidate = new CandidateRestaurantMenu(
            restId, "Gourmet Healthy", "Gourmet Healthy", 4.9m, 100, false, false, 0, 10,
            [lockedDinner, otherDinner, lunchChoice]);

        var context = new MealRecommendationContext(
            Guid.NewGuid(), Guid.NewGuid(), _targetDate, CurrentRestaurantId: restId,
            MealPreferenceFilter.Empty, null, slots, History: [], [candidate],
            LockedMealIds: new HashSet<Guid> { lockedDinner.MealId });

        var result = await _engine.RecommendBoxForDayAsync(context);

        result.Should().NotBeNull();
        result!.SelectedMeals.Should().HaveCount(2);
        result.SelectedMeals.Should().Contain(x => x.MealId == lockedDinner.MealId);
        result.SelectedMeals.Should().Contain(x => x.MealId == lunchChoice.MealId);
    }

    [Fact]
    public async Task RecommendBoxForDayAsync_GeneratesProteinGoalAndDiversityHighlights()
    {
        var restId = Guid.NewGuid();
        var slots = new List<SlotRequirementDto> { new("Lunch", 1) };

        var chickenLunch = CreateMeal(Guid.NewGuid(), "Grilled Chicken", "Lunch", 450);

        var candidate = new CandidateRestaurantMenu(restId, "Protein House", "Protein House", 4.8m, 50, false, false, 0, 10, [chickenLunch]);

        var target = new ProgramDailyNutritionTargetDto(
            Guid.NewGuid(), "FIT", Guid.NewGuid(), "STD", 0, 1, 0, 0, 1,
            SuggestedKcal: 450, SuggestedProteinG: 30, SuggestedCarbsG: 45, SuggestedFatG: 12, SuggestedFiberG: 4,
            KcalMin: 400, KcalMax: 500, ProteinGMin: 25, ProteinGMax: 35, CarbsGMin: 30, CarbsGMax: 60, FatGMin: 10, FatGMax: 20,
            CarbMode: "Standard", KetoDayNetCarbsLimit: null, StandardVersion: "v1");

        var context = new MealRecommendationContext(
            Guid.NewGuid(), Guid.NewGuid(), _targetDate, CurrentRestaurantId: null,
            MealPreferenceFilter.Empty, target, slots, History: [], [candidate]);

        var result = await _engine.RecommendBoxForDayAsync(context);

        result.Should().NotBeNull();
        result!.MatchHighlights.Should().Contain("Balanced Protein Diversity");
        result.MatchHighlights.Should().Contain("Meets Target Protein Goal");
    }
}
