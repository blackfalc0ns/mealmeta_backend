namespace MealMate.Application.Features.DriverDelivery.CustomerTracking;

/// <summary>
/// Durable invalidation for a customer delivery day. It contains identifiers only;
/// consumers must re-read current authorized state before sending it to a client.
/// </summary>
public sealed record CustomerDeliveryChangedV1(
    Guid EventId,
    Guid SubscriptionDayId,
    DateTime OccurredAtUtc,
    int SchemaVersion = 1)
{
    public const string OutboxEventType = "CustomerDeliveryChangedV1";
}
