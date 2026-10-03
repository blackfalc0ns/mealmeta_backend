namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Dispatches kitchen-ready notifications to delivery managers (dispatchers) immediately
/// for each new ready order, with a short cooldown as a burst guard (e.g. during batch
/// label printing), delivering FCM push alerts and real-time updates.
/// </summary>
public interface IBatchDispatchNotifier
{
    /// <summary>
    /// Alerts the dispatcher as soon as a new ready order is pending (threshold 1),
    /// subject to the short burst-guard cooldown.
    /// </summary>
    Task NotifyIfBatchThresholdReachedAsync(
        Guid restaurantId,
        Guid subscriptionDayId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks for unassigned orders that remain ready below the batch threshold (e.g. 1 to 4 orders)
    /// past the debounce window (e.g. 10 minutes) and dispatches a summary alert so orders are not stranded.
    /// </summary>
    Task CheckAndNotifyHangingBatchesAsync(
        Guid restaurantId,
        CancellationToken cancellationToken = default);
}

public sealed class NullBatchDispatchNotifier : IBatchDispatchNotifier
{
    public static readonly NullBatchDispatchNotifier Instance = new();

    public Task NotifyIfBatchThresholdReachedAsync(
        Guid restaurantId,
        Guid subscriptionDayId,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task CheckAndNotifyHangingBatchesAsync(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
