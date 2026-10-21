namespace MealMate.Application.Features.RestaurantOperations.Availability;

/// <summary>
/// Immutable snapshot of operational timing policy.
/// </summary>
public sealed record TimingSnapshot(long Version, int OperationalLeadHours);

/// <summary>
/// A restaurant delivery slot with start and end times.
/// </summary>
public sealed record DeliverySlot(DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>
/// Core unified operational timing engine governing cutoff calculations,
/// earliest eligible first delivery thresholds, and plan calendar visibility.
/// </summary>
public static class ServiceTimingPolicy
{
    /// <summary>
    /// Calculates the absolute cutoff deadline before which customer selections,
    /// delivery address edits, and freeze requests for this slot must be completed.
    /// </summary>
    public static DateTimeOffset Cutoff(DeliverySlot slot, TimingSnapshot policy)
        => slot.StartsAt.AddHours(-policy.OperationalLeadHours);

    /// <summary>
    /// Calculates the earliest eligible delivery timestamp based on payment time and unified lead hours.
    /// </summary>
    public static DateTimeOffset EarliestEligibleThreshold(DateTimeOffset paidAt, TimingSnapshot policy)
        => paidAt.AddHours(policy.OperationalLeadHours);

    /// <summary>
    /// Checks if a delivery slot satisfies the operational lead time requirement relative to payment time.
    /// </summary>
    public static bool IsSlotEligible(DateTimeOffset paidAt, DeliverySlot slot, TimingSnapshot policy)
        => slot.StartsAt >= EarliestEligibleThreshold(paidAt, policy);

    /// <summary>
    /// Determines whether a calendar day is active/visible according to the customer's subscription plan.
    /// Non-service days (such as Friday when not selected in the plan) must not appear in the calendar.
    /// </summary>
    public static bool IsCalendarDayVisible(DayOfWeek dayOfWeek, bool includeFriday)
    {
        if (dayOfWeek == DayOfWeek.Friday && !includeFriday)
            return false;

        return true;
    }

    /// <summary>
    /// Generates the sequence of eligible first delivery calendar dates starting from payment time.
    /// Filters out non-service days (like excluded Friday) and dates that fall within the lead time cutoff.
    /// </summary>
    public static IEnumerable<DateOnly> GetEligibleFirstDeliveryDates(
        DateTimeOffset paidAt,
        TimingSnapshot policy,
        bool includeFriday,
        TimeOnly defaultDeliveryStartTime,
        TimeSpan timeZoneOffset,
        int lookaheadDays = 14)
    {
        var threshold = EarliestEligibleThreshold(paidAt, policy);
        var paymentLocalDate = DateOnly.FromDateTime(paidAt.ToOffset(timeZoneOffset).DateTime);

        for (int i = 0; i <= lookaheadDays; i++)
        {
            var candidateDate = paymentLocalDate.AddDays(i);

            // Filter non-service days (e.g. Friday if excluded from plan)
            if (!IsCalendarDayVisible(candidateDate.DayOfWeek, includeFriday))
                continue;

            var slotStart = new DateTimeOffset(candidateDate.ToDateTime(defaultDeliveryStartTime), timeZoneOffset);
            if (slotStart >= threshold)
            {
                yield return candidateDate;
            }
        }
    }
}
