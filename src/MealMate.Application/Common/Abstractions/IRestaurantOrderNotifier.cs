namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Notifies restaurant staff when a subscription day order is dispatched at −72h.
/// </summary>
public interface IRestaurantOrderNotifier
{
    Task NotifyOrderSentToRestaurantAsync(
        Guid restaurantId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
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
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NotifyKitchenBatchReadyAsync(
        Guid restaurantId,
        Guid subscriptionDayId,
        int boxCount,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
