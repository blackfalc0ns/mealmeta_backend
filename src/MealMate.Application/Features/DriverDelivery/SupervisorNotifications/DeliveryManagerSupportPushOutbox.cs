using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Common;
using MealMate.Domain.IdentityAccess;

namespace MealMate.Application.Features.DriverDelivery.SupervisorNotifications;

/// <summary>Save the inbox item and its push request in the caller's transaction.</summary>
public static class DeliveryManagerSupportPushOutbox
{
    public const string EventType = "delivery-manager-support-push.v1";

    public static void Add(IAppDbContext db, DeliveryManagerNotification notification)
    {
        db.DeliveryManagerNotifications.Add(notification);
        db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = EventType,
            AggregateType = nameof(DeliveryManagerNotification),
            AggregateId = notification.Id.ToString("D"),
            DeduplicationKey = $"delivery-manager-support-push:{notification.Id:N}",
            CreatedAtUtc = notification.SentAtUtc,
            PayloadJson = JsonSerializer.Serialize(new Payload(notification.Id))
        });
    }

    public sealed record Payload(Guid NotificationId);
}
