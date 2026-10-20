using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MediatR;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed class DriverSupportHandoverOutboxHandler(IDriverSupportHandoverNotifier notifier)
    : INotificationHandler<OutboxLifecycleNotification>
{
    public async Task Handle(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        if (!string.Equals(notification.EventType, "driver-support-handover-changed.v1", StringComparison.Ordinal)) return;
        var payload = JsonSerializer.Deserialize<Payload>(notification.PayloadJson)
                      ?? throw new InvalidOperationException("Driver support handover event payload is invalid.");
        await notifier.PublishAsync(new DriverSupportHandoverEvent(notification.MessageId, notification.CreatedAtUtc,
            payload.HandoverId, payload.TripId, payload.ReplacementTripId, payload.RestaurantId,
            payload.OriginalDriverProfileId, payload.ReplacementDriverProfileId, payload.Status, payload.Event), cancellationToken);
    }

    private sealed record Payload(Guid HandoverId, Guid TripId, Guid? ReplacementTripId, Guid RestaurantId,
        Guid OriginalDriverProfileId, Guid ReplacementDriverProfileId, string Status, string Event);
}
