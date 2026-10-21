using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MealMate.Application.Features.DriverDelivery.SupervisorNotifications;
using MealMate.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Push;

/// <summary>FCM delivery runs from the durable outbox, independently of SignalR listeners.</summary>
public sealed class DeliveryManagerSupportPushHandler(
    IAppDbContext db, IFcmMessagingClient fcm, ILogger<DeliveryManagerSupportPushHandler> logger)
    : INotificationHandler<OutboxLifecycleNotification>
{
    public async Task Handle(OutboxLifecycleNotification message, CancellationToken cancellationToken)
    {
        if (message.EventType != DeliveryManagerSupportPushOutbox.EventType) return;
        var payload = JsonSerializer.Deserialize<DeliveryManagerSupportPushOutbox.Payload>(message.PayloadJson)
            ?? throw new InvalidOperationException("Delivery manager push payload is invalid.");
        var item = await db.DeliveryManagerNotifications.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == payload.NotificationId, cancellationToken);
        if (item?.RestaurantId is not Guid restaurantId) return;
        if (!await db.DeliveryManagerRegistrations.AsNoTracking().AnyAsync(x =>
                x.RestaurantId == restaurantId && x.ResultingUserId == item.UserId
                && x.Status == VerificationStatus.Approved && !x.IsDeleted, cancellationToken)) return;

        var devices = await db.DeliveryManagerDeviceTokens.Where(x => x.UserId == item.UserId && x.IsActive
                && !x.IsDeleted && (x.RestaurantId == null || x.RestaurantId == restaurantId))
            .ToListAsync(cancellationToken);
        if (devices.Count == 0)
        {
            logger.LogWarning("No active device for delivery manager support notification {NotificationId}.", item.Id);
            return;
        }
        if (!fcm.IsConfigured)
            throw new InvalidOperationException("FCM is not configured for delivery manager support pushes.");

        var data = new Dictionary<string, string>
        {
            ["eventId"] = message.MessageId.ToString("D"),
            ["notificationId"] = item.Id.ToString("D"),
            ["restaurantId"] = restaurantId.ToString("D"),
            ["eventKey"] = item.Type,
            ["type"] = item.Type,
            ["section"] = item.Section ?? string.Empty,
            ["subjectId"] = item.SubjectId?.ToString("D") ?? string.Empty,
            ["deepLink"] = item.DeepLink ?? string.Empty,
            ["action"] = item.DeepLink ?? string.Empty,
            ["titleAr"] = item.TitleAr, ["titleEn"] = item.TitleEn,
            ["bodyAr"] = item.BodyAr, ["bodyEn"] = item.BodyEn
        };
        var failed = false;
        foreach (var group in devices.GroupBy(x => x.Token))
        {
            var result = await fcm.SendAsync(group.Key, item.TitleAr, item.BodyAr, data, cancellationToken);
            if (result.IsInvalidToken)
            {
                foreach (var device in group) device.IsActive = false;
                await db.SaveChangesAsync(cancellationToken);
                logger.LogWarning("Deactivated invalid device for support notification {NotificationId}.", item.Id);
            }
            else if (!result.IsSuccess)
            {
                failed = true;
                logger.LogWarning("FCM rejected support notification {NotificationId}; outbox will retry.", item.Id);
            }
        }
        if (failed) throw new InvalidOperationException("FCM delivery of a delivery manager support notification failed.");
    }
}
