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
        snapshot.LMin.Should().Be(3);
        snapshot.LMax.Should().Be(26);
    }

    [Fact]
    public void CalculateLimit_30Restaurants_26Days_ClampsToHardMin3()
    {
        var subscriptionId = Guid.NewGuid();
        var durationDays = 26;
        var availableRestaurants = 30;

        var snapshot = _calculator.CalculateLimit(subscriptionId, durationDays, availableRestaurants);

        snapshot.SubscriptionId.Should().Be(subscriptionId);
        snapshot.DurationDays.Should().Be(26);
        snapshot.NSnapshot.Should().Be(30);
        snapshot.LMin.Should().Be(3);
        snapshot.LMax.Should().Be(8);
        snapshot.LimitValue.Should().Be(3);
        snapshot.RawLimit.Should().BeApproximately(0.8667m, 0.001m);
    }

    [Fact]
    public void CalculateLimit_5Restaurants_26Days_ClampsComputedToAdminCeiling()
    {
        var subscriptionId = Guid.NewGuid();
        var durationDays = 26;
        var availableRestaurants = 5;

        var snapshot = _calculator.CalculateLimit(subscriptionId, durationDays, availableRestaurants, adminCeiling: 8);

        snapshot.DurationDays.Should().Be(26);
        snapshot.NSnapshot.Should().Be(5);
        snapshot.LMin.Should().Be(3);
        snapshot.LMax.Should().Be(8);
        snapshot.LimitValue.Should().Be(6);
        snapshot.RawLimit.Should().Be(5.2m);
    }

    [Fact]
    public void CalculateLimit_HighComputed_RaisesCeilingToCoverAverageDays()
    {
        var snapshot = _calculator.CalculateLimit(Guid.NewGuid(), durationDays: 26, availableRestaurantsCount: 2, adminCeiling: 5);

        snapshot.LimitValue.Should().Be(13);
        snapshot.LMin.Should().Be(3);
        snapshot.LMax.Should().Be(13);
    }

    [Theory]
    [InlineData(26, 2, 13)]
    [InlineData(26, 3, 9)]
    [InlineData(26, 4, 7)]
    [InlineData(30, 2, 15)]
    [InlineData(6, 4, 3)]
    public void CalculateLimit_CoversAllServiceDays(int days, int restaurants, int expected)
    {
        var snapshot = _calculator.CalculateLimit(Guid.NewGuid(), days, restaurants);
        snapshot.LimitValue.Should().Be(expected);
        (snapshot.LimitValue * restaurants).Should().BeGreaterThanOrEqualTo(days);
        _calculator.IsSelectionAllowed(expected - 1, snapshot.LimitValue).Should().BeTrue();
        _calculator.IsSelectionAllowed(expected, snapshot.LimitValue).Should().BeFalse();
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
