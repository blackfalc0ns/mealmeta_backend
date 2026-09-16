using FluentAssertions;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignFulfillmentAllocatorTests
{
    private readonly CampaignFulfillmentAllocator _allocator = new();

    [Fact]
    public void Allocate_WhenAllDaysHaveSufficientCapacity_DiversifiesAcrossRestaurants()
    {
        var restA = Guid.NewGuid();
        var restB = Guid.NewGuid();
        var dates = Enumerable.Range(1, 10).Select(d => new DateOnly(2026, 10, d)).ToList();

        var capacityList = new List<RestaurantDailyAvailability>();
        foreach (var d in dates)
        {
            capacityList.Add(new RestaurantDailyAvailability(restA, d, AvailableCapacity: 5, TierRank: 1));
            capacityList.Add(new RestaurantDailyAvailability(restB, d, AvailableCapacity: 5, TierRank: 2));
        }

        var result = _allocator.Allocate(dates, capacityList);

        result.IsSuccess.Should().BeTrue();
        result.Allocations.Should().HaveCount(10);
        result.Allocations.Select(a => a.ServiceDate).Should().BeEquivalentTo(dates);

        // Check diversification: both restaurants should have received allocations
        var countA = result.Allocations.Count(a => a.RestaurantId == restA);
        var countB = result.Allocations.Count(a => a.RestaurantId == restB);
        countA.Should().Be(5);
        countB.Should().Be(5);
    }

    [Fact]
    public void Allocate_WhenOneDayHasZeroCapacityAcrossAllRestaurants_FailsCompletely()
    {
        var restA = Guid.NewGuid();
        var restB = Guid.NewGuid();
        var dates = Enumerable.Range(1, 5).Select(d => new DateOnly(2026, 10, d)).ToList();

        var capacityList = new List<RestaurantDailyAvailability>();
        foreach (var d in dates)
        {
            // Day 3 has 0 capacity for both
            var cap = (d.Day == 3) ? 0 : 5;
            capacityList.Add(new RestaurantDailyAvailability(restA, d, AvailableCapacity: cap, TierRank: 1));
            capacityList.Add(new RestaurantDailyAvailability(restB, d, AvailableCapacity: cap, TierRank: 2));
        }

        var result = _allocator.Allocate(dates, capacityList);

        result.IsSuccess.Should().BeFalse();
        result.Allocations.Should().BeEmpty();
        result.FailureReason.Should().Contain("2026-10-03");
    }

    [Fact]
    public void Allocate_RespectsIndividualRestaurantDailyCapacities()
    {
        var restA = Guid.NewGuid();
        var restB = Guid.NewGuid();
        var dates = Enumerable.Range(1, 4).Select(d => new DateOnly(2026, 10, d)).ToList();

        var capacityList = new List<RestaurantDailyAvailability>
        {
            // Day 1: both available
            new(restA, dates[0], AvailableCapacity: 2, TierRank: 1),
            new(restB, dates[0], AvailableCapacity: 2, TierRank: 2),
            // Day 2: only Rest B available
            new(restA, dates[1], AvailableCapacity: 0, TierRank: 1),
            new(restB, dates[1], AvailableCapacity: 2, TierRank: 2),
            // Day 3: only Rest A available
            new(restA, dates[2], AvailableCapacity: 2, TierRank: 1),
            new(restB, dates[2], AvailableCapacity: 0, TierRank: 2),
            // Day 4: both available
            new(restA, dates[3], AvailableCapacity: 2, TierRank: 1),
            new(restB, dates[3], AvailableCapacity: 2, TierRank: 2)
        };

        var result = _allocator.Allocate(dates, capacityList);

        result.IsSuccess.Should().BeTrue();
        result.Allocations.Single(a => a.ServiceDate == dates[1]).RestaurantId.Should().Be(restB);
        result.Allocations.Single(a => a.ServiceDate == dates[2]).RestaurantId.Should().Be(restA);
    }

    [Fact]
    public void Allocate_IsDeterministic()
    {
        var restA = Guid.NewGuid();
        var restB = Guid.NewGuid();
        var dates = Enumerable.Range(1, 14).Select(d => new DateOnly(2026, 10, d)).ToList();

        var capacityList = new List<RestaurantDailyAvailability>();
        foreach (var d in dates)
        {
            capacityList.Add(new RestaurantDailyAvailability(restA, d, AvailableCapacity: 10, TierRank: 1));
            capacityList.Add(new RestaurantDailyAvailability(restB, d, AvailableCapacity: 10, TierRank: 2));
        }

        var run1 = _allocator.Allocate(dates, capacityList);
        var run2 = _allocator.Allocate(dates, capacityList);

        run1.IsSuccess.Should().BeTrue();
        run2.IsSuccess.Should().BeTrue();
        run1.Allocations.Should().BeEquivalentTo(run2.Allocations, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Allocate_WhenRequestedDaysEmpty_ReturnsEmptySuccess()
    {
        var result = _allocator.Allocate(Array.Empty<DateOnly>(), Array.Empty<RestaurantDailyAvailability>());

        result.IsSuccess.Should().BeTrue();
        result.Allocations.Should().BeEmpty();
    }
}
