using FluentAssertions;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class SubscriberCapManagerTests
{
    private readonly SubscriberCapManager _manager = new();

    [Fact]
    public void Reserve_WhenCapNotExceeded_IncrementsReservedSlots()
    {
        var state = new SubscriberCapState(MaxSubscribers: 10, ReservedSlots: 3, CommittedSlots: 5);

        var result = _manager.TryReserve(state, slotsToReserve: 1);

        result.IsSuccess.Should().BeTrue();
        result.NewState.ReservedSlots.Should().Be(4);
        result.NewState.CommittedSlots.Should().Be(5);
    }

    [Fact]
    public void Reserve_WhenCapExceeded_ReturnsFailure()
    {
        var state = new SubscriberCapState(MaxSubscribers: 10, ReservedSlots: 4, CommittedSlots: 6);

        var result = _manager.TryReserve(state, slotsToReserve: 1);

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Contain("Subscriber cap reached");
        result.NewState.Should().Be(state);
    }

    [Fact]
    public void Reserve_WhenCapUnlimited_AlwaysSucceeds()
    {
        var state = new SubscriberCapState(MaxSubscribers: null, ReservedSlots: 100, CommittedSlots: 500);

        var result = _manager.TryReserve(state, slotsToReserve: 5);

        result.IsSuccess.Should().BeTrue();
        result.NewState.ReservedSlots.Should().Be(105);
    }

    [Fact]
    public void Release_DecrementsReservedSlots_WithoutDroppingBelowZero()
    {
        var state = new SubscriberCapState(MaxSubscribers: 10, ReservedSlots: 2, CommittedSlots: 5);

        var newState = _manager.Release(state, slotsToRelease: 2);
        newState.ReservedSlots.Should().Be(0);

        // Releasing more than reserved clamps to 0
        var clampedState = _manager.Release(state, slotsToRelease: 5);
        clampedState.ReservedSlots.Should().Be(0);
    }

    [Fact]
    public void Commit_ConvertsReservedToCommitted_MaintainsTotal()
    {
        var state = new SubscriberCapState(MaxSubscribers: 10, ReservedSlots: 2, CommittedSlots: 5);

        var result = _manager.TryCommit(state, slotsToCommit: 2);

        result.IsSuccess.Should().BeTrue();
        result.NewState.ReservedSlots.Should().Be(0);
        result.NewState.CommittedSlots.Should().Be(7);
    }

    [Fact]
    public void Commit_WhenReservedSlotsInsufficient_ReturnsFailure()
    {
        var state = new SubscriberCapState(MaxSubscribers: 10, ReservedSlots: 1, CommittedSlots: 5);

        var result = _manager.TryCommit(state, slotsToCommit: 2);

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Contain("insufficient reserved slots");
    }

    [Fact]
    public void CanReserve_ReturnsAccurateAvailability()
    {
        var cappedState = new SubscriberCapState(MaxSubscribers: 10, ReservedSlots: 5, CommittedSlots: 5);
        _manager.CanReserve(cappedState, 1).Should().BeFalse();

        var availableState = new SubscriberCapState(MaxSubscribers: 10, ReservedSlots: 4, CommittedSlots: 5);
        _manager.CanReserve(availableState, 1).Should().BeTrue();
        _manager.CanReserve(availableState, 2).Should().BeFalse();
    }
}
