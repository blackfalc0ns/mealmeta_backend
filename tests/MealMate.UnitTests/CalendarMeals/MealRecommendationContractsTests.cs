using FluentAssertions;
using MealMate.Application.Features.CalendarMeals;
using MealMate.Application.Features.CustomerCheckout;
using Xunit;

namespace MealMate.UnitTests.CalendarMeals;

public sealed class MealRecommendationContractsTests
{
    [Fact]
    public void MealRecommendationContext_Instantiates_Correctly()
    {
        var context = new MealRecommendationContext(
            CustomerUserId: Guid.NewGuid(),
            SubscriptionId: Guid.NewGuid(),
            ServiceDate: new DateOnly(2026, 9, 15),
            CurrentRestaurantId: null,
            Preferences: MealPreferenceFilter.Empty,
            NutritionTarget: null,
            RequiredSlots: [],
            History: [],
            Candidates: []);

        context.CustomerUserId.Should().NotBeEmpty();
        context.ServiceDate.Should().Be(new DateOnly(2026, 9, 15));
    }
}
