namespace MealMate.Application.Features.RestaurantOperations.Availability;

public sealed record ConfirmationDeadlineSnapshot(
    Guid OrderId,
    Guid RestaurantId,
    DateTime DispatchedAtUtc,
    int SlaHours,
    DateTime DeadlineUtc,
    DateTime HalfwayReminderUtc,
    DateTime FinalNoticeUtc);

/// <summary>
/// Computes and tracks SLA deadlines and alert thresholds for manual order confirmation by restaurants.
/// Default SLA is 4 hours (configurable by admin between 1 and 8 hours).
/// Alerts fire at 50% of SLA and 30 minutes before deadline expiration.
/// </summary>
public static class RestaurantConfirmationDeadlineService
{
    public const int DefaultSlaHours = 4;

    public static ConfirmationDeadlineSnapshot ComputeDeadline(
        Guid orderId,
        Guid restaurantId,
        DateTime dispatchedAtUtc,
        int slaHours = DefaultSlaHours)
    {
        var effectiveHours = Math.Clamp(slaHours, 1, 8);
        var deadline = dispatchedAtUtc.AddHours(effectiveHours);
        var halfway = dispatchedAtUtc.AddMinutes(effectiveHours * 30);
        var finalNotice = deadline.AddMinutes(-30);

        if (finalNotice <= halfway)
        {
            finalNotice = dispatchedAtUtc.AddMinutes(effectiveHours * 45);
        }

        return new ConfirmationDeadlineSnapshot(
            orderId,
            restaurantId,
            dispatchedAtUtc,
            effectiveHours,
            deadline,
            halfway,
            finalNotice);
    }

    public static bool IsOverdue(DateTime currentUtc, ConfirmationDeadlineSnapshot snapshot)
        => currentUtc >= snapshot.DeadlineUtc;

    public static bool NeedsHalfwayReminder(DateTime currentUtc, ConfirmationDeadlineSnapshot snapshot)
        => currentUtc >= snapshot.HalfwayReminderUtc && currentUtc < snapshot.FinalNoticeUtc;

    public static bool NeedsFinalNotice(DateTime currentUtc, ConfirmationDeadlineSnapshot snapshot)
        => currentUtc >= snapshot.FinalNoticeUtc && currentUtc < snapshot.DeadlineUtc;
}
