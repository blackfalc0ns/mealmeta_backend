using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Common;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.IdentityAccess;

namespace MealMate.Application.Features.UnifiedSupport;

/// <summary>Persists a requester inbox item and its push request in the caller's transaction.</summary>
public static class SupportCasePushOutbox
{
    public const string EventType = "unified-support-case-push.v1";
    private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void Add(IAppDbContext db, CustomerNotification notification, Guid supportEventId)
    {
        db.CustomerNotifications.Add(notification);
        AddOutbox(db, notification.Id, notification.UserId, supportEventId, "customer", notification.CreatedAtUtc);
    }

    public static void Add(IAppDbContext db, DriverNotification notification, Guid supportEventId)
    {
        db.DriverNotifications.Add(notification);
        AddOutbox(db, notification.Id, notification.UserId, supportEventId, "driver", notification.CreatedAtUtc);
    }

    private static void AddOutbox(IAppDbContext db, Guid notificationId, Guid recipientUserId,
        Guid supportEventId, string audience, DateTime createdAtUtc)
    {
        db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = EventType,
            AggregateType = nameof(SupportCaseEvent),
            AggregateId = supportEventId.ToString("D"),
            DeduplicationKey = $"unified-support-case-push:{notificationId:N}",
            CreatedAtUtc = createdAtUtc,
            PayloadJson = JsonSerializer.Serialize(new Payload(notificationId, supportEventId, recipientUserId, audience), PayloadOptions),
        });
    }

    public sealed record Payload(Guid NotificationId, Guid SupportEventId, Guid RecipientUserId, string Audience);
}
