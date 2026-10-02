namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Notifies restaurant staff when a subscription day order is dispatched at −72h.
/// </summary>
public interface IRestaurantOrderNotifier
{
    /// <param name="isAutoSelected">
    /// True when the box was picked by the system after the customer's selection
    /// deadline (or by the customer pressing auto-select and reaching cutoff),
    /// false when the customer reviewed and confirmed the day themselves.
    /// Controls the notification wording so restaurant staff can tell them apart.
    /// </param>
    Task NotifyOrderSentToRestaurantAsync(
        Guid restaurantId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        bool isAutoSelected = false,
        CancellationToken cancellationToken = default);

    Task NotifyKitchenBatchReadyAsync(
        Guid restaurantId,
        Guid subscriptionDayId,
        int boxCount,
        CancellationToken cancellationToken = default);
}

public sealed class NullRestaurantOrderNotifier : IRestaurantOrderNotifier
{
    public static readonly NullRestaurantOrderNotifier Instance = new();

    public Task NotifyOrderSentToRestaurantAsync(
        Guid restaurantId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        bool isAutoSelected = false,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NotifyKitchenBatchReadyAsync(
        Guid restaurantId,
        Guid subscriptionDayId,
        int boxCount,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
