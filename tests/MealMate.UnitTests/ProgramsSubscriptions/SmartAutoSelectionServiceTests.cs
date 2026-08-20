using FluentAssertions;
using MealMate.Application.Features.CalendarMeals;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public sealed class SmartAutoSelectionServiceTests
{
    private readonly ISmartAutoSelectionService _service = new SmartAutoSelectionService();

    [Fact]
    public void FilterSafeMeals_ExcludesMealsWithCustomerAllergens()
    {
        var customerAllergenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Peanuts", "Gluten" };

        var candidateMeals = new List<CandidateMealOption>
        {
            new(Guid.NewGuid(), "Safe Rice & Grilled Chicken", "Breakfast", ["Rice", "Chicken"], []),
            new(Guid.NewGuid(), "Peanut Butter Oats", "Breakfast", ["Oats", "Peanut Butter"], ["Peanuts"]),
            new(Guid.NewGuid(), "Wheat Bread Toast", "Breakfast", ["Wheat"], ["Gluten"]),
            new(Guid.NewGuid(), "Fresh Fruit Salad", "Breakfast", ["Apple", "Orange"], [])
        };

        var safeMeals = _service.FilterSafeMeals(candidateMeals, customerAllergenNames);

        safeMeals.Should().HaveCount(2);
        safeMeals.Select(x => x.MealName).Should().BeEquivalentTo(["Safe Rice & Grilled Chicken", "Fresh Fruit Salad"]);
    }

    [Fact]
    public void AutoSelectDailyMeals_PicksSafeMealsMatchingRequiredSlots()
    {
        var customerAllergenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Dairy" };
        var requiredSlots = new[] { "Breakfast", "Lunch", "Snack" };

        var candidateMeals = new List<CandidateMealOption>
        {
            new(Guid.NewGuid(), "Omelette with Cheese", "Breakfast", ["Eggs", "Cheese"], ["Dairy"]),
            new(Guid.NewGuid(), "Boiled Eggs & Salad", "Breakfast", ["Eggs", "Lettuce"], []),
            new(Guid.NewGuid(), "Beef Steak with Rice", "Lunch", ["Beef", "Rice"], []),
            new(Guid.NewGuid(), "Greek Yogurt Bowl", "Snack", ["Yogurt"], ["Dairy"]),
            new(Guid.NewGuid(), "Mixed Roasted Nuts", "Snack", ["Almonds", "Walnuts"], [])
        };

        var selected = _service.AutoSelectMealsForDay(candidateMeals, customerAllergenNames, requiredSlots);

        selected.Should().HaveCount(3);
        selected.Select(x => x.Slot).Should().BeEquivalentTo(["Breakfast", "Lunch", "Snack"]);
        selected.Should().NotContain(x => x.Allergens.Contains("Dairy", StringComparer.OrdinalIgnoreCase));
    }
}
