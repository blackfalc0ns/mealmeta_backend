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

    Task NotifyOrderConfirmedByRestaurantAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default);

    Task NotifyDeliveryPrepReminderAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default);

    Task NotifyOrderOutForDeliveryAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Driver confirmed arrival at the customer's delivery location. Sent by the
    /// backend only after the arrival state is saved (first successful arrival).
    /// </summary>
    Task NotifyDriverArrivedAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default);

    Task NotifyOrderDeliveredAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default);

    Task NotifyInfluencerPromotedAsync(
        Guid customerUserId,
        string influencerCode,
        decimal commissionRate,
        CancellationToken cancellationToken = default);

    Task NotifyOrderReplacedAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        string newRestaurantName,
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

    public Task NotifyOrderConfirmedByRestaurantAsync(
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

    public Task NotifyOrderOutForDeliveryAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NotifyDriverArrivedAsync(
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

    public Task NotifyInfluencerPromotedAsync(
        Guid customerUserId,
        string influencerCode,
        decimal commissionRate,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NotifyOrderReplacedAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        string newRestaurantName,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
