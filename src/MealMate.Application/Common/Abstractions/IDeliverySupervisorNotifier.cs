namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Urgent escalation alerts for the trip's delivery managers (مسئولي التوصيل):
/// raised when a delivery stop is actually late. Lifecycle milestones
/// (pickup completed / trip delivered) go to the restaurant via
/// <see cref="IRestaurantDeliveryNotifier"/> instead — they concern the
/// restaurant's batch, not the delivery supervisor.
/// Failures must be logged and not block the originating business workflow.
/// </summary>
public interface IDeliverySupervisorNotifier
{
    Task NotifyStopDelayedAsync(
        Guid restaurantId,
        Guid stopId,
        Guid tripId,
        string tripCode,
        string driverNameAr,
        string driverNameEn,
        string orderCode,
        string boxCode,
        string zoneNameAr,
        string zoneNameEn,
        int minutesLate,
        CancellationToken cancellationToken = default);
}
