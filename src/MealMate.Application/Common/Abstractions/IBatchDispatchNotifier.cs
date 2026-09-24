namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Throttles and dispatches kitchen-ready notifications to delivery managers (dispatchers).
/// Aggregates notifications into batches (e.g. 5 orders ready) to avoid notification fatigue,
/// delivering FCM push alerts and real-time updates.
/// </summary>
public interface IBatchDispatchNotifier
{
    /// <summary>
    /// Evaluates if newly ready orders for a restaurant have reached the batch threshold (5, 10, ...)
    /// and dispatches FCM push alerts and real-time events while respecting cooldown.
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
