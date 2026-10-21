using FluentAssertions;
using MealMate.Application.Features.RestaurantOperations.Availability;
using Xunit;

namespace MealMate.UnitTests.Capacity;

public class ServiceTimingPolicyTests
{
    private readonly TimingSnapshot _defaultPolicy = new(Version: 1, OperationalLeadHours: 72);
    private readonly TimeSpan _kuwaitOffset = TimeSpan.FromHours(3);
    private readonly TimeOnly _defaultDeliveryStartTime = new(13, 0); // 1:00 PM

    [Fact]
    public void Cutoff_CalculatesExactLeadTimeBeforeSlotStart()
    {
        var slotStart = new DateTimeOffset(2026, 10, 22, 13, 0, 0, _kuwaitOffset); // Thursday 13:00
        var slot = new DeliverySlot(slotStart, slotStart.AddHours(2));

        var cutoff = ServiceTimingPolicy.Cutoff(slot, _defaultPolicy);

        // 72 hours before Thursday 13:00 is Monday 13:00
        cutoff.Should().Be(new DateTimeOffset(2026, 10, 19, 13, 0, 0, _kuwaitOffset));
    }

    [Fact]
    public void MondayPayment_AllowsThursdayDelivery()
    {
        // Paid Monday 2026-10-19 at 11:00 AM Kuwait time
        var paidMonday = new DateTimeOffset(2026, 10, 19, 11, 0, 0, _kuwaitOffset);
        var thursdaySlot = new DeliverySlot(
            new DateTimeOffset(2026, 10, 22, 13, 0, 0, _kuwaitOffset), // Thursday 13:00
            new DateTimeOffset(2026, 10, 22, 15, 0, 0, _kuwaitOffset)
        );

        var isEligible = ServiceTimingPolicy.IsSlotEligible(paidMonday, thursdaySlot, _defaultPolicy);

        isEligible.Should().BeTrue("Thursday 13:00 is 74 hours after Monday 11:00 (>= 72h lead time)");
    }

    [Fact]
    public void MondayPayment_WednesdaySlotIsNotEligible()
    {
        // Paid Monday 2026-10-19 at 11:00 AM Kuwait time
        var paidMonday = new DateTimeOffset(2026, 10, 19, 11, 0, 0, _kuwaitOffset);
        var wednesdaySlot = new DeliverySlot(
            new DateTimeOffset(2026, 10, 21, 13, 0, 0, _kuwaitOffset), // Wednesday 13:00 (only 50 hours lead)
            new DateTimeOffset(2026, 10, 21, 15, 0, 0, _kuwaitOffset)
        );

        var isEligible = ServiceTimingPolicy.IsSlotEligible(paidMonday, wednesdaySlot, _defaultPolicy);

        isEligible.Should().BeFalse("Wednesday 13:00 is only 50 hours after Monday 11:00 (< 72h lead time)");
    }

    [Fact]
    public void MondayPayment_SelectingFriday_DoesNotPostponeThursday()
    {
        // Paid Monday 11:00 AM
        var paidMonday = new DateTimeOffset(2026, 10, 19, 11, 0, 0, _kuwaitOffset);

        // When includeFriday is true, Thursday is still eligible as the earliest service day
        var eligibleDates = ServiceTimingPolicy.GetEligibleFirstDeliveryDates(
            paidMonday,
            _defaultPolicy,
            includeFriday: true,
            _defaultDeliveryStartTime,
            _kuwaitOffset,
            lookaheadDays: 7
        ).ToList();

        eligibleDates.First().Should().Be(new DateOnly(2026, 10, 22), "Thursday must be the first eligible date even when Friday is selected");
        eligibleDates.Should().Contain(new DateOnly(2026, 10, 23), "Friday must also be present as an available choice");
    }

    [Fact]
    public void MondayPayment_WithoutFriday_OmitsFridayAndJumpsToSaturday()
    {
        // Paid Monday 11:00 AM
        var paidMonday = new DateTimeOffset(2026, 10, 19, 11, 0, 0, _kuwaitOffset);

        // When includeFriday is false, Friday must be completely omitted from the calendar
        var eligibleDates = ServiceTimingPolicy.GetEligibleFirstDeliveryDates(
            paidMonday,
            _defaultPolicy,
            includeFriday: false,
            _defaultDeliveryStartTime,
            _kuwaitOffset,
            lookaheadDays: 7
        ).ToList();

        eligibleDates.First().Should().Be(new DateOnly(2026, 10, 22), "Thursday is the earliest eligible delivery day");
        eligibleDates.Should().NotContain(new DateOnly(2026, 10, 23), "Friday must NOT appear in the calendar for non-Friday plans");
        eligibleDates.Skip(1).First().Should().Be(new DateOnly(2026, 10, 24), "Next available delivery day after Thursday must jump directly to Saturday");
    }

    [Fact]
    public void TuesdayPayment_WithoutFriday_FirstAvailableDayIsSaturday()
    {
        // Paid Tuesday 2026-10-20 at 11:00 AM Kuwait time
        var paidTuesday = new DateTimeOffset(2026, 10, 20, 11, 0, 0, _kuwaitOffset);

        // Thursday is < 72h (only 50h). Friday is 74h, but Friday is excluded in plan!
        var eligibleDates = ServiceTimingPolicy.GetEligibleFirstDeliveryDates(
            paidTuesday,
            _defaultPolicy,
            includeFriday: false,
            _defaultDeliveryStartTime,
            _kuwaitOffset,
            lookaheadDays: 7
        ).ToList();

        eligibleDates.Should().NotContain(new DateOnly(2026, 10, 22), "Thursday is within 72h lead time");
        eligibleDates.Should().NotContain(new DateOnly(2026, 10, 23), "Friday must NOT appear in the calendar");
        eligibleDates.First().Should().Be(new DateOnly(2026, 10, 24), "First available delivery day must be Saturday");
    }

    [Fact]
    public void TuesdayPayment_WithFriday_FirstAvailableDayIsFriday()
    {
        // Paid Tuesday 2026-10-20 at 11:00 AM Kuwait time
        var paidTuesday = new DateTimeOffset(2026, 10, 20, 11, 0, 0, _kuwaitOffset);

        // With Friday included, Friday (74h >= 72h) is eligible and appears first!
        var eligibleDates = ServiceTimingPolicy.GetEligibleFirstDeliveryDates(
            paidTuesday,
            _defaultPolicy,
            includeFriday: true,
            _defaultDeliveryStartTime,
            _kuwaitOffset,
            lookaheadDays: 7
        ).ToList();

        eligibleDates.First().Should().Be(new DateOnly(2026, 10, 23), "Friday is the earliest eligible delivery day for Tuesday payment when Friday is included");
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday, true, true)]
    [InlineData(DayOfWeek.Sunday, false, true)]
    [InlineData(DayOfWeek.Monday, true, true)]
    [InlineData(DayOfWeek.Monday, false, true)]
    [InlineData(DayOfWeek.Thursday, true, true)]
    [InlineData(DayOfWeek.Thursday, false, true)]
    [InlineData(DayOfWeek.Friday, true, true)]
    [InlineData(DayOfWeek.Friday, false, false)]
    [InlineData(DayOfWeek.Saturday, true, true)]
    [InlineData(DayOfWeek.Saturday, false, true)]
    public void IsCalendarDayVisible_FollowsPlanConfiguration(DayOfWeek day, bool includeFriday, bool expectedVisible)
    {
        var visible = ServiceTimingPolicy.IsCalendarDayVisible(day, includeFriday);
        visible.Should().Be(expectedVisible);
    }
}
