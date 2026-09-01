namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Sends Firebase push notifications to customer mobile devices.
/// Failures must not block the originating business workflow.
/// </summary>
public interface ICustomerPushNotifier
{
    Task NotifySubscriptionPaymentConfirmedAsync(
        Guid customerUserId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default);

    Task NotifyOrderLockedAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default);

    Task NotifyDeliveryPrepReminderAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default);

    Task NotifyOrderDeliveredAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default);
}

public sealed class NullCustomerPushNotifier : ICustomerPushNotifier
{
    public static readonly NullCustomerPushNotifier Instance = new();

    public Task NotifySubscriptionPaymentConfirmedAsync(
        Guid customerUserId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NotifyOrderLockedAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NotifyDeliveryPrepReminderAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NotifyOrderDeliveredAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
