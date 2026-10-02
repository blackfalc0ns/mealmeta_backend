namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Consolidated delivery-supervisor (مسئول التوصيل) notifications about a
/// driver's progress. Deliberately coarse-grained: one alert when pickup of all
/// boxes is done, one when the whole trip is delivered, and an urgent alert
/// only for stops that are actually late — never per box.
/// Failures must be logged and not block the originating business workflow.
/// </summary>
public interface IDeliverySupervisorNotifier
{
    Task NotifyPickupCompletedAsync(
        Guid tripId,
        string tripCode,
        string driverNameAr,
        string driverNameEn,
        int boxesCount,
        CancellationToken cancellationToken = default);

    Task NotifyTripDeliveredAsync(
        Guid tripId,
        string tripCode,
        string driverNameAr,
        string driverNameEn,
        int boxesCount,
        CancellationToken cancellationToken = default);

    Task NotifyStopDelayedAsync(
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

public sealed class NullDeliverySupervisorNotifier : IDeliverySupervisorNotifier
{
    public static readonly NullDeliverySupervisorNotifier Instance = new();

    public Task NotifyPickupCompletedAsync(Guid tripId, string tripCode, string driverNameAr, string driverNameEn, int boxesCount, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NotifyTripDeliveredAsync(Guid tripId, string tripCode, string driverNameAr, string driverNameEn, int boxesCount, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NotifyStopDelayedAsync(Guid stopId, Guid tripId, string tripCode, string driverNameAr, string driverNameEn, string orderCode, string boxCode, string zoneNameAr, string zoneNameEn, int minutesLate, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
