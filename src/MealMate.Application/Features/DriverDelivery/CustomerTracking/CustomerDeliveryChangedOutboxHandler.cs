using System.Text.Json;
using MealMate.Application.Common.Outbox;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.DriverDelivery.CustomerTracking;

/// <summary>Relays the outbox invalidation over the shared transport used by every API instance.</summary>
public sealed class CustomerDeliveryChangedOutboxHandler(
    ICustomerDeliveryChangeTransport transport,
    ILogger<CustomerDeliveryChangedOutboxHandler> logger)
    : INotificationHandler<CustomerDeliveryChangedOutboxNotification>
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task Handle(CustomerDeliveryChangedOutboxNotification deliveryNotification, CancellationToken cancellationToken)
    {
        var notification = deliveryNotification.Message;
        if (!string.Equals(notification.EventType, CustomerDeliveryChangedV1.OutboxEventType, StringComparison.Ordinal))
            return;

        CustomerDeliveryChangedV1? change;
        try
        {
            change = JsonSerializer.Deserialize<CustomerDeliveryChangedV1>(notification.PayloadJson, PayloadOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Ignoring malformed customer delivery change outbox message {MessageId}.", notification.MessageId);
            return;
        }

        if (change is null || change.SchemaVersion != 1 || change.EventId != notification.MessageId
            || change.SubscriptionDayId == Guid.Empty)
        {
            logger.LogWarning(
                "Ignoring unsupported or inconsistent customer delivery change outbox message {MessageId} with schema {SchemaVersion}.",
                notification.MessageId,
                change?.SchemaVersion);
            return;
        }

        // The outbox message id is the stable idempotency key across retries.
        var canonicalChange = change with { EventId = notification.MessageId };
        await transport.PublishAsync(canonicalChange, cancellationToken);
    }
}
