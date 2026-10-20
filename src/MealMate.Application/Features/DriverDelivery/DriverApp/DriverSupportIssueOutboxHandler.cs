using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MediatR;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed class DriverSupportIssueOutboxHandler(IDriverSupportIssueNotifier notifier)
    : INotificationHandler<OutboxLifecycleNotification>
{
    public async Task Handle(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        if (!string.Equals(notification.EventType, "driver-support-issue-changed.v1", StringComparison.Ordinal)) return;
        var payload = JsonSerializer.Deserialize<Payload>(notification.PayloadJson)
                      ?? throw new InvalidOperationException("Driver support issue event payload is invalid.");
        await notifier.PublishAsync(new DriverSupportIssueEvent(notification.MessageId, notification.CreatedAtUtc,
            payload.IssueId, payload.RestaurantId, payload.DriverProfileId, payload.Category, payload.Event), cancellationToken);
    }

    private sealed record Payload(Guid IssueId, Guid RestaurantId, Guid? DriverProfileId, string Category, string Event);
}
