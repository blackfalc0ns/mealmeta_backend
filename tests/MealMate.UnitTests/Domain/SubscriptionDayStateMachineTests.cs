using FluentAssertions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using Xunit;

namespace MealMate.UnitTests.Domain;

public class SubscriptionDayStateMachineTests
{
    [Theory]
    [InlineData(SubscriptionDayStatus.Open, SubscriptionDayStatus.CustomerSelected)]
    [InlineData(SubscriptionDayStatus.Open, SubscriptionDayStatus.AutoSelected)]
    [InlineData(SubscriptionDayStatus.CustomerSelected, SubscriptionDayStatus.Locked72h)]
    [InlineData(SubscriptionDayStatus.Locked72h, SubscriptionDayStatus.SentToRestaurant)]
    [InlineData(SubscriptionDayStatus.SentToRestaurant, SubscriptionDayStatus.ConfirmedByRestaurant)]
    [InlineData(SubscriptionDayStatus.ConfirmedByRestaurant, SubscriptionDayStatus.Preparing)]
    [InlineData(SubscriptionDayStatus.Preparing, SubscriptionDayStatus.OutForDelivery)]
    [InlineData(SubscriptionDayStatus.OutForDelivery, SubscriptionDayStatus.Delivered)]
    public void CanTransitionTo_ValidTransitions_ReturnsTrue(SubscriptionDayStatus from, SubscriptionDayStatus to)
    {
        var day = new SubscriptionDay { Status = from };
        day.CanTransitionTo(to).Should().BeTrue();

        var result = day.TransitionTo(to);
        result.IsSuccess.Should().BeTrue();
        day.Status.Should().Be(to);
    }

    [Theory]
    [InlineData(SubscriptionDayStatus.Open, SubscriptionDayStatus.Delivered)]
    [InlineData(SubscriptionDayStatus.Open, SubscriptionDayStatus.OutForDelivery)]
    [InlineData(SubscriptionDayStatus.Locked72h, SubscriptionDayStatus.OutForDelivery)]
    [InlineData(SubscriptionDayStatus.ConfirmedByRestaurant, SubscriptionDayStatus.Delivered)]
    [InlineData(SubscriptionDayStatus.Delivered, SubscriptionDayStatus.Open)]
    [InlineData(SubscriptionDayStatus.Cancelled, SubscriptionDayStatus.Preparing)]
    public void CanTransitionTo_InvalidTransitions_ReturnsFalse_AndDoesNotMutateStatus(SubscriptionDayStatus from, SubscriptionDayStatus to)
    {
        var day = new SubscriptionDay { Status = from };
        day.CanTransitionTo(to).Should().BeFalse();

        var result = day.TransitionTo(to);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().StartWith("INVALID_STATE_TRANSITION");
        day.Status.Should().Be(from, "Invalid transition must not mutate entity state.");
    }
}
