using FluentAssertions;
using MealMate.Application.Features.ProgramsSubscriptions;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public sealed class RestaurantDiversificationCalculatorTests
{
    private readonly IRestaurantDiversificationCalculator _calculator = new RestaurantDiversificationCalculator();

    [Fact]
    public void CalculateLimit_SingleRestaurant_AllowsAllDays()
    {
        var subscriptionId = Guid.NewGuid();
        var durationDays = 26;
        var availableRestaurants = 1;

        var snapshot = _calculator.CalculateLimit(subscriptionId, durationDays, availableRestaurants);

        snapshot.SubscriptionId.Should().Be(subscriptionId);
        snapshot.DurationDays.Should().Be(26);
        snapshot.NSnapshot.Should().Be(1);
        snapshot.LimitValue.Should().Be(26);
        snapshot.RawLimit.Should().Be(26m);
        snapshot.LMin.Should().Be(1);
        snapshot.LMax.Should().Be(26);
    }

    [Fact]
    public void CalculateLimit_30Restaurants_26Days_CalculatesDiversifiedQuota()
    {
        var subscriptionId = Guid.NewGuid();
        var durationDays = 26;
        var availableRestaurants = 30;

        // ceil(26 / 30) = 1, + margin(1) = 2
        var snapshot = _calculator.CalculateLimit(subscriptionId, durationDays, availableRestaurants, margin: 1);

        snapshot.SubscriptionId.Should().Be(subscriptionId);
        snapshot.DurationDays.Should().Be(26);
        snapshot.NSnapshot.Should().Be(30);
        snapshot.LimitValue.Should().Be(2); // At most 2 days per restaurant
        snapshot.RawLimit.Should().BeApproximately(0.8667m, 0.001m);
    }

    [Fact]
    public void CalculateLimit_5Restaurants_26Days_CalculatesBalancedQuota()
    {
        var subscriptionId = Guid.NewGuid();
        var durationDays = 26;
        var availableRestaurants = 5;

        // ceil(26 / 5) = 6, + margin(1) = 7
        var snapshot = _calculator.CalculateLimit(subscriptionId, durationDays, availableRestaurants, margin: 1);

        snapshot.DurationDays.Should().Be(26);
        snapshot.NSnapshot.Should().Be(5);
        snapshot.LimitValue.Should().Be(7);
        snapshot.RawLimit.Should().Be(5.2m);
    }

    [Fact]
    public void ValidateRestaurantSelectionQuota_WhenExceedsLimit_ReturnsFalse()
    {
        var limitValue = 2;
        var alreadySelectedDaysForRestaurant = 2;

        var allowed = _calculator.IsSelectionAllowed(alreadySelectedDaysForRestaurant, limitValue);

        allowed.Should().BeFalse();
    }

    [Fact]
    public void ValidateRestaurantSelectionQuota_WhenUnderLimit_ReturnsTrue()
    {
        var limitValue = 2;
        var alreadySelectedDaysForRestaurant = 1;

        var allowed = _calculator.IsSelectionAllowed(alreadySelectedDaysForRestaurant, limitValue);

        allowed.Should().BeTrue();
    }
}
