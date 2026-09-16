using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignReservationCoordinatorTests
{
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly SubscriberCapManager _capManager = new();
    private readonly CampaignFulfillmentAllocator _allocator = new();
    private readonly CampaignReservationCoordinator _coordinator;

    public CampaignReservationCoordinatorTests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc));
        _coordinator = new CampaignReservationCoordinator(_capManager, _allocator, _clock);
    }

    [Fact]
    public void CoordinateReservation_WhenCapAndCapacityAvailable_SucceedsWithAllocationsAndLockTargets()
    {
        var campaignId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var restA = Guid.NewGuid();
        var restB = Guid.NewGuid();

        var dates = new List<DateOnly>
        {
            new(2026, 10, 2),
            new(2026, 10, 3)
        };

        var capState = new SubscriberCapState(MaxSubscribers: 10, ReservedSlots: 1, CommittedSlots: 2);

        var capacities = new List<RestaurantDailyAvailability>
        {
            new(restA, dates[0], AvailableCapacity: 2, TierRank: 1),
            new(restB, dates[0], AvailableCapacity: 2, TierRank: 2),
            new(restA, dates[1], AvailableCapacity: 2, TierRank: 1),
            new(restB, dates[1], AvailableCapacity: 2, TierRank: 2)
        };

        var result = _coordinator.CoordinateReservation(new ReservationRequest(
            campaignId,
            bundleId,
            customerId,
            capState,
            dates,
            capacities,
            ReservationTtl: TimeSpan.FromMinutes(15)));

        result.IsSuccess.Should().BeTrue();
        result.Allocations.Should().HaveCount(2);
        result.UpdatedCapState!.ReservedSlots.Should().Be(2); // Incremented from 1
        result.ExpiresAtUtc.Should().Be(_clock.UtcNow.AddMinutes(15));
        result.DeterministicLockTargets.Should().HaveCount(2);
    }

    [Fact]
    public void CoordinateReservation_WhenSubscriberCapExceeded_FailsWithoutAllocatingCapacity()
    {
        var campaignId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var capState = new SubscriberCapState(MaxSubscribers: 5, ReservedSlots: 2, CommittedSlots: 3); // Fully booked

        var result = _coordinator.CoordinateReservation(new ReservationRequest(
            campaignId,
            bundleId,
            customerId,
            capState,
            new List<DateOnly> { new(2026, 10, 2) },
            Array.Empty<RestaurantDailyAvailability>(),
            ReservationTtl: TimeSpan.FromMinutes(15)));

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Contain("Subscriber cap reached");
        result.Allocations.Should().BeEmpty();
    }

    [Fact]
    public void CoordinateReservation_WhenDailyCapacityUnavailable_FailsWithoutReservingSubscriberSlot()
    {
        var campaignId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var restA = Guid.NewGuid();

        var dates = new List<DateOnly> { new(2026, 10, 2) };
        var capState = new SubscriberCapState(MaxSubscribers: 10, ReservedSlots: 1, CommittedSlots: 2);

        // 0 capacity on requested date
        var capacities = new List<RestaurantDailyAvailability>
        {
            new(restA, dates[0], AvailableCapacity: 0, TierRank: 1)
        };

        var result = _coordinator.CoordinateReservation(new ReservationRequest(
            campaignId,
            bundleId,
            customerId,
            capState,
            dates,
            capacities,
            ReservationTtl: TimeSpan.FromMinutes(15)));

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Contain("unavailable on date");
        result.UpdatedCapState.Should().BeNull();
        result.Allocations.Should().BeEmpty();
    }

    [Fact]
    public void CoordinateReservation_ReturnsDeterministicSortedLockTargets()
    {
        var campaignId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var r1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var r2 = Guid.Parse("00000000-0000-0000-0000-000000000002");

        // Pass dates in reverse order
        var d1 = new DateOnly(2026, 10, 5);
        var d2 = new DateOnly(2026, 10, 2);

        var capState = new SubscriberCapState(MaxSubscribers: 10, ReservedSlots: 0, CommittedSlots: 0);

        var capacities = new List<RestaurantDailyAvailability>
        {
            new(r1, d1, AvailableCapacity: 5),
            new(r2, d2, AvailableCapacity: 5)
        };

        var result = _coordinator.CoordinateReservation(new ReservationRequest(
            campaignId,
            bundleId,
            customerId,
            capState,
            new List<DateOnly> { d1, d2 },
            capacities,
            ReservationTtl: TimeSpan.FromMinutes(15)));

        result.IsSuccess.Should().BeTrue();
        // Deterministic targets must be strictly sorted by (ServiceDate, RestaurantId)
        result.DeterministicLockTargets.Should().BeInAscendingOrder(t => t.ServiceDate);
        result.DeterministicLockTargets.First().ServiceDate.Should().Be(d2);
        result.DeterministicLockTargets.Last().ServiceDate.Should().Be(d1);
    }
}
