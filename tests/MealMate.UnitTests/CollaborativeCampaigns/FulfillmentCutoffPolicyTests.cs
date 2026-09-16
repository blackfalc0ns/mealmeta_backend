using FluentAssertions;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class FulfillmentCutoffPolicyTests
{
    private readonly FulfillmentCutoffPolicy _policy = new();
    private readonly DateTime _now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsLocked_WhenBeforeCutoffWindow_ReturnsFalse()
    {
        var serviceDate = new DateOnly(2026, 10, 8); // 3 days away
        var cutoff = TimeSpan.FromHours(48);

        var isLocked = _policy.IsLocked(serviceDate, _now, cutoff);

        isLocked.Should().BeFalse();
    }

    [Fact]
    public void IsLocked_WhenWithinCutoffWindow_ReturnsTrue()
    {
        var serviceDate = new DateOnly(2026, 10, 6); // 1 day away
        var cutoff = TimeSpan.FromHours(48);

        var isLocked = _policy.IsLocked(serviceDate, _now, cutoff);

        isLocked.Should().BeTrue();
    }

    [Fact]
    public void CanModifySelection_WhenDayUnlockedAndBeforeDeadline_ReturnsTrue()
    {
        var day = new SubscriptionDay
        {
            ServiceDate = new DateOnly(2026, 10, 8),
            Status = SubscriptionDayStatus.CustomerSelected,
            SelectionDeadlineUtc = _now.AddDays(1)
        };

        var result = _policy.CanModifySelection(day, _now);

        result.CanModify.Should().BeTrue();
    }

    [Fact]
    public void CanModifySelection_WhenDayAlreadyLocked_ReturnsFalse()
    {
        var day = new SubscriptionDay
        {
            ServiceDate = new DateOnly(2026, 10, 8),
            Status = SubscriptionDayStatus.Locked72h,
            SelectionDeadlineUtc = _now.AddDays(1)
        };

        var result = _policy.CanModifySelection(day, _now);

        result.CanModify.Should().BeFalse();
        result.Reason.Should().Contain("locked");
    }

    [Fact]
    public void CanModifySelection_WhenPastDeadline_ReturnsFalse()
    {
        var day = new SubscriptionDay
        {
            ServiceDate = new DateOnly(2026, 10, 8),
            Status = SubscriptionDayStatus.Open,
            SelectionDeadlineUtc = _now.AddHours(-1) // Deadline passed
        };

        var result = _policy.CanModifySelection(day, _now);

        result.CanModify.Should().BeFalse();
        result.Reason.Should().Contain("deadline");
    }

    [Fact]
    public void EnforceCutoffLock_WhenWithinCutoffWindow_LocksDayAndSetsLockTimestamp()
    {
        var day = new SubscriptionDay
        {
            ServiceDate = new DateOnly(2026, 10, 6),
            Status = SubscriptionDayStatus.CustomerSelected
        };

        var locked = _policy.EnforceCutoffLock(day, _now, TimeSpan.FromHours(48));

        locked.Should().BeTrue();
        day.Status.Should().Be(SubscriptionDayStatus.Locked72h);
        day.LockAtUtc.Should().Be(_now);
    }
}
