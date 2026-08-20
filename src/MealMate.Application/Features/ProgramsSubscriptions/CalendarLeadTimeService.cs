namespace MealMate.Application.Features.ProgramsSubscriptions;

public interface ICalendarLeadTimeService
{
    DateOnly CalculateFirstDeliveryDate(
        DateTime paymentUtc,
        int leadTimeHours = 48,
        IReadOnlyCollection<DayOfWeek>? activeDays = null);

    DateOnly CalculateFirstDeliveryDate(
        DateTime paymentUtc,
        int leadTimeHours,
        bool includeFriday);

    DateTime CalculateDailyLockDeadline(
        DateOnly deliveryDate,
        int lockCutoffHourUtc = 10,
        int lockAdvanceDays = 2);
}

public sealed class CalendarLeadTimeService : ICalendarLeadTimeService
{
    public const int DefaultLeadTimeHours = 48;
    public const int DefaultLockAdvanceDays = 2;
    public const int DefaultLockCutoffHourUtc = 10;

    public DateOnly CalculateFirstDeliveryDate(
        DateTime paymentUtc,
        int leadTimeHours = DefaultLeadTimeHours,
        IReadOnlyCollection<DayOfWeek>? activeDays = null)
    {
        if (leadTimeHours < 0) leadTimeHours = DefaultLeadTimeHours;

        var earliestUtc = paymentUtc.AddHours(leadTimeHours);
        var targetDate = DateOnly.FromDateTime(earliestUtc);

        if (activeDays is null || activeDays.Count == 0)
        {
            // By default in MealMate, Friday is an off-day unless explicitly requested
            if (targetDate.DayOfWeek == DayOfWeek.Friday)
            {
                return targetDate.AddDays(1); // Advance to Saturday
            }
            return targetDate;
        }

        var activeSet = new HashSet<DayOfWeek>(activeDays);
        for (var i = 0; i < 14; i++)
        {
            if (activeSet.Contains(targetDate.DayOfWeek))
            {
                return targetDate;
            }
            targetDate = targetDate.AddDays(1);
        }

        return targetDate;
    }

    public DateOnly CalculateFirstDeliveryDate(
        DateTime paymentUtc,
        int leadTimeHours,
        bool includeFriday)
    {
        var activeDays = includeFriday
            ? new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday }
            : new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Saturday };

        return CalculateFirstDeliveryDate(paymentUtc, leadTimeHours, activeDays);
    }

    public DateTime CalculateDailyLockDeadline(
        DateOnly deliveryDate,
        int lockCutoffHourUtc = DefaultLockCutoffHourUtc,
        int lockAdvanceDays = DefaultLockAdvanceDays)
    {
        var targetLockDate = deliveryDate.AddDays(-lockAdvanceDays);
        return new DateTime(targetLockDate.Year, targetLockDate.Month, targetLockDate.Day, lockCutoffHourUtc, 0, 0, DateTimeKind.Utc);
    }
}
