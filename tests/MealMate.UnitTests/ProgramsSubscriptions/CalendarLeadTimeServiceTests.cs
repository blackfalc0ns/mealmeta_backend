using FluentAssertions;
using MealMate.Application.Features.ProgramsSubscriptions;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public sealed class CalendarLeadTimeServiceTests
{
    private readonly ICalendarLeadTimeService _service = new CalendarLeadTimeService();

    [Fact]
    public void CalculateFirstDeliveryDate_48HoursLeadTime_ReturnsExpectedDate()
    {
        // Payment on Monday at 10:00 AM UTC
        var paymentUtc = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc); // Monday

        // +48h = Wednesday 2026-09-16
        var firstDelivery = _service.CalculateFirstDeliveryDate(paymentUtc, leadTimeHours: 48);

        firstDelivery.Should().Be(new DateOnly(2026, 9, 16));
    }

    [Fact]
    public void CalculateFirstDeliveryDate_72HoursLeadTime_ReturnsExpectedDate()
    {
        // Payment on Monday at 10:00 AM UTC
        var paymentUtc = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc); // Monday

        // +72h = Thursday 2026-09-17
        var firstDelivery = _service.CalculateFirstDeliveryDate(paymentUtc, leadTimeHours: 72);

        firstDelivery.Should().Be(new DateOnly(2026, 9, 17));
    }

    [Fact]
    public void CalculateFirstDeliveryDate_WhenFridayNotIncluded_AutomaticallyAdvancesToSaturday()
    {
        // Payment on Wednesday at 10:00 AM UTC (2026-09-16)
        var paymentUtc = new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc); // Wednesday
        // +48h = Friday (2026-09-18)

        // With includeFriday = false
        var firstDeliveryWithoutFriday = _service.CalculateFirstDeliveryDate(paymentUtc, leadTimeHours: 48, includeFriday: false);
        // Friday is skipped -> advances to Saturday 2026-09-19
        firstDeliveryWithoutFriday.Should().Be(new DateOnly(2026, 9, 19));

        // With includeFriday = true
        var firstDeliveryWithFriday = _service.CalculateFirstDeliveryDate(paymentUtc, leadTimeHours: 48, includeFriday: true);
        firstDeliveryWithFriday.Should().Be(new DateOnly(2026, 9, 18)); // Friday itself
    }

    [Fact]
    public void CalculateFirstDeliveryDate_SkipsNonActiveDays()
    {
        // Payment on Wednesday at 10:00 AM UTC (2026-09-16)
        var paymentUtc = new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc); // Wednesday
        // +48h = Friday (2026-09-18)
        // If Friday and Saturday are off-days (active days: Sunday to Thursday)
        var activeDays = new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday };

        var firstDelivery = _service.CalculateFirstDeliveryDate(paymentUtc, leadTimeHours: 48, activeDays: activeDays);

        // Friday skipped -> Saturday skipped -> Sunday 2026-09-20
        firstDelivery.Should().Be(new DateOnly(2026, 9, 20));
    }

    [Fact]
    public void CalculateDailyLockDeadline_ReturnsConfiguredHoursBeforeDelivery()
    {
        var deliveryDate = new DateOnly(2026, 9, 20); // Sunday
        // Lock 48 hours prior at 10:00 AM UTC
        var lockUtc = _service.CalculateDailyLockDeadline(deliveryDate, lockCutoffHourUtc: 10, lockAdvanceDays: 2);

        lockUtc.Should().Be(new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc));
    }
}
