using FluentAssertions;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignCapacityCalendarInitializerTests
{
    private readonly CampaignCapacityCalendarInitializer _initializer = new();

    [Fact]
    public void Initialize_GeneratesCalendarCoveringFullHorizon_WithCorrectDatesAndCapacities()
    {
        var campaignId = Guid.NewGuid();
        var salesStart = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var salesEnd = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc); // 10 days sales window
        var maxDurationDays = 26;
        var bufferDays = 1;

        var restA = Guid.NewGuid();
        var restB = Guid.NewGuid();
        var restaurants = new List<RestaurantCapacityInput>
        {
            new(restA, 15),
            new(restB, 25)
        };

        var result = _initializer.Initialize(campaignId, salesStart, salesEnd, maxDurationDays, restaurants, bufferDays);

        var expectedStartDate = new DateOnly(2026, 10, 1);
        var expectedEndDate = new DateOnly(2026, 10, 10).AddDays(maxDurationDays + bufferDays); // Oct 10 + 27 = Nov 6
        var expectedDaysCount = (expectedEndDate.DayNumber - expectedStartDate.DayNumber) + 1;

        result.StartDate.Should().Be(expectedStartDate);
        result.HorizonEndDate.Should().Be(expectedEndDate);
        result.TotalDays.Should().Be(expectedDaysCount);
        result.Capacities.Should().HaveCount(expectedDaysCount * 2);

        // Every day should have records for both restaurants with 0 reserved boxes
        var dayRecords = result.Capacities.Where(c => c.ServiceDate == expectedStartDate).ToList();
        dayRecords.Should().HaveCount(2);
        dayRecords.Should().Contain(c => c.RestaurantId == restA && c.MaxBoxes == 15 && c.ReservedBoxes == 0);
        dayRecords.Should().Contain(c => c.RestaurantId == restB && c.MaxBoxes == 25 && c.ReservedBoxes == 0);
    }

    [Fact]
    public void Initialize_WhenServiceDayPredicateProvided_OnlyIncludesMatchingDays()
    {
        var campaignId = Guid.NewGuid();
        var salesStart = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var salesEnd = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
        var restaurants = new List<RestaurantCapacityInput> { new(Guid.NewGuid(), 10) };

        // Exclude Fridays (DayOfWeek == Friday)
        var result = _initializer.Initialize(
            campaignId,
            salesStart,
            salesEnd,
            maxSubscriptionDurationDays: 14,
            restaurants,
            bufferDays: 1,
            isServiceDayPredicate: date => date.DayOfWeek != DayOfWeek.Friday);

        result.Capacities.Should().NotContain(c => c.ServiceDate!.Value.DayOfWeek == DayOfWeek.Friday);
    }

    [Fact]
    public void Initialize_WhenSalesEndPrecedesSalesStart_ThrowsArgumentException()
    {
        var campaignId = Guid.NewGuid();
        var salesStart = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
        var salesEnd = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        var action = () => _initializer.Initialize(
            campaignId,
            salesStart,
            salesEnd,
            maxSubscriptionDurationDays: 26,
            new List<RestaurantCapacityInput> { new(Guid.NewGuid(), 10) });

        action.Should().Throw<ArgumentException>()
            .WithMessage("*sales start date must precede sales end date*");
    }

    [Fact]
    public void Initialize_WhenNoRestaurantsProvided_ReturnsEmptyCapacities()
    {
        var campaignId = Guid.NewGuid();
        var salesStart = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var salesEnd = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);

        var result = _initializer.Initialize(
            campaignId,
            salesStart,
            salesEnd,
            maxSubscriptionDurationDays: 26,
            new List<RestaurantCapacityInput>());

        result.Capacities.Should().BeEmpty();
        result.TotalCapacitySlots.Should().Be(0);
    }

    [Fact]
    public void Initialize_WhenMaxDurationZeroOrNegative_ThrowsArgumentOutOfRangeException()
    {
        var campaignId = Guid.NewGuid();
        var salesStart = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var salesEnd = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);

        var action = () => _initializer.Initialize(
            campaignId,
            salesStart,
            salesEnd,
            maxSubscriptionDurationDays: 0,
            new List<RestaurantCapacityInput> { new(Guid.NewGuid(), 10) });

        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
