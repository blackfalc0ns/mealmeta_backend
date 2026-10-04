using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.DriverDelivery.SupervisorNotifications;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Push;

/// <summary>
/// Persists urgent delivery-delay escalations to the trip restaurant's delivery
/// managers (مسئولي التوصيل) in their in-app bell and fans them out over FCM to
/// their active devices. Neither the platform admin nor the restaurant owner
/// bell receives these — the delivery supervisor owns the delivery-delay alarm.
/// Every step is best-effort: notification failures must never break the
/// delivery workflow that raised them.
/// </summary>
public sealed class DeliverySupervisorNotifier : IDeliverySupervisorNotifier
{
    private readonly IAppDbContext _db;
    private readonly IFcmMessagingClient _fcm;
    private readonly ILogger<DeliverySupervisorNotifier> _logger;

    public DeliverySupervisorNotifier(
        IAppDbContext db,
        IFcmMessagingClient fcm,
        ILogger<DeliverySupervisorNotifier> logger)
    {
        _db = db;
        _fcm = fcm;
        _logger = logger;
    }

    public async Task NotifyStopDelayedAsync(
        Guid restaurantId,
        Guid stopId,
        Guid tripId,
        string tripCode,
        string driverNameAr,
        string driverNameEn,
        string orderCode,
        string boxCode,
        string zoneNameAr,
        string zoneNameEn,
        int minutesLate,
        CancellationToken cancellationToken = default)
    {
        var titleAr = "تأخير عاجل في التسليم ⚠️";
        var titleEn = "Urgent Delivery Delay";
        var bodyAr = $"تأخير {minutesLate} دقيقة في تسليم الطلب {orderCode}"
                     + (string.IsNullOrWhiteSpace(zoneNameAr) ? "." : $" - منطقة {zoneNameAr}.")
                     + (string.IsNullOrWhiteSpace(driverNameAr) ? string.Empty : $" السائق: {driverNameAr}.");
        var bodyEn = $"Order {orderCode} is {minutesLate} minutes past its delivery window"
                     + (string.IsNullOrWhiteSpace(zoneNameEn) ? "." : $" ({zoneNameEn}).")
                     + (string.IsNullOrWhiteSpace(driverNameEn) ? string.Empty : $" Driver: {driverNameEn}.");

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "delivery.stop.delayed",
            ["priority"] = "urgent",
            ["stopId"] = stopId.ToString(),
            ["tripId"] = tripId.ToString(),
            ["tripCode"] = tripCode,
            ["orderCode"] = orderCode,
            ["boxCode"] = boxCode,
            ["minutesLate"] = minutesLate.ToString()
        };

        await NotifyDeliveryManagersAsync(
            restaurantId, stopId, tripId, titleAr, titleEn, bodyAr, bodyEn, data, cancellationToken);
    }

    private async Task NotifyDeliveryManagersAsync(
        Guid restaurantId,
        Guid stopId,
        Guid tripId,
        string titleAr,
        string titleEn,
        string bodyAr,
        string bodyEn,
        Dictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        List<Guid> userIds;
        try
        {
            userIds = await _db.DeliveryManagerRegistrations
                .AsNoTracking()
                .Where(d => d.RestaurantId == restaurantId
                            && d.ResultingUserId.HasValue
                            && !d.IsDeleted)
                .Select(d => d.ResultingUserId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve delivery managers for {EventKey} of restaurant {RestaurantId}.",
                data.GetValueOrDefault("eventKey"), restaurantId);
            return;
        }

        if (userIds.Count == 0)
        {
            _logger.LogInformation(
                "No delivery managers registered for restaurant {RestaurantId}; delay alert {EventKey} dropped.",
                restaurantId, data.GetValueOrDefault("eventKey"));
            return;
        }

        try
        {
            foreach (var userId in userIds)
            {
                _db.DeliveryManagerNotifications.Add(new DeliveryManagerNotification
                {
                    UserId = userId,
                    RestaurantId = restaurantId,
                    Type = DeliveryDelayEscalationService.DelayNotificationType,
                    TitleAr = titleAr,
                    TitleEn = titleEn,
                    BodyAr = bodyAr,
                    BodyEn = bodyEn,
                    Section = "delivery",
                    SubjectId = stopId,
                    DeepLink = $"/trips/{tripId}",
                    IsRead = false,
                    SentAtUtc = DateTime.UtcNow
                });
            }
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist supervisor notification {EventKey}.", data.GetValueOrDefault("eventKey"));
            return;
        }

        if (!_fcm.IsConfigured)
            return;

        List<string> tokens;
        try
        {
            tokens = await _db.DeliveryManagerDeviceTokens
                .AsNoTracking()
                .Where(x => x.IsActive && userIds.Contains(x.UserId))
                .Select(x => x.Token)
                .Distinct()
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load delivery manager device tokens for {EventKey}.", data.GetValueOrDefault("eventKey"));
            return;
        }

        foreach (var token in tokens)
        {
            try
            {
                await _fcm.SendAsync(token, titleAr, bodyAr, data, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send supervisor push for {EventKey}.", data.GetValueOrDefault("eventKey"));
            }
        }
    }
}
