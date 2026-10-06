using MealMate.Application.Common.Abstractions;
using MealMate.Domain.IdentityAccess;
using MealMate.Infrastructure.Push;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Api.Realtime;

/// <summary>
/// Persists driver delivery lifecycle alerts (batch pickup completed, trip fully
/// delivered) to the restaurant staff bell and fans them out over SignalR and FCM,
/// mirroring <see cref="SignalRRestaurantOrderNotifier"/>. Every push step is
/// best-effort: notification failures must never break the delivery workflow.
/// </summary>
public sealed class SignalRRestaurantDeliveryNotifier : IRestaurantDeliveryNotifier
{
    private readonly IHubContext<RestaurantReviewHub> _hubContext;
    private readonly IAppDbContext _db;
    private readonly IFcmMessagingClient? _fcm;
    private readonly ILogger<SignalRRestaurantDeliveryNotifier>? _logger;

    public SignalRRestaurantDeliveryNotifier(
        IHubContext<RestaurantReviewHub> hubContext,
        IAppDbContext db,
        IFcmMessagingClient? fcm = null,
        ILogger<SignalRRestaurantDeliveryNotifier>? logger = null)
    {
        _hubContext = hubContext;
        _db = db;
        _fcm = fcm;
        _logger = logger;
    }

    public async Task NotifyPickupCompletedAsync(
        Guid restaurantId,
        Guid tripId,
        string tripCode,
        string driverNameAr,
        string driverNameEn,
        int boxesCount,
        CancellationToken cancellationToken = default)
    {
        var titleAr = "اكتمل مسح الصناديق ✅";
        var titleEn = "Pickup Completed";
        var bodyAr = string.IsNullOrWhiteSpace(driverNameAr)
            ? $"تم مسح وتصوير كل الصناديق ({boxesCount}) الخاصة بالرحلة {tripCode} وجاهزة للتوصيل."
            : $"أنهى السائق {driverNameAr} مسح وتصوير كل الصناديق ({boxesCount}) للرحلة {tripCode}.";
        var bodyEn = string.IsNullOrWhiteSpace(driverNameEn)
            ? $"All {boxesCount} boxes of trip {tripCode} were scanned and photographed — ready for delivery."
            : $"Driver {driverNameEn} finished scanning and photographing all {boxesCount} boxes of trip {tripCode}.";

        await NotifyAsync(
            restaurantId, tripId, "trip-pickup-completed",
            "/restaurant/orders/handover", "orders/handover",
            titleAr, titleEn, bodyAr, bodyEn,
            new Dictionary<string, string>
            {
                ["eventKey"] = "driver.pickup.completed",
                ["tripId"] = tripId.ToString(),
                ["tripCode"] = tripCode,
                ["boxesCount"] = boxesCount.ToString()
            },
            cancellationToken);
    }

    public async Task NotifyTripDeliveredAsync(
        Guid restaurantId,
        Guid tripId,
        string tripCode,
        string driverNameAr,
        string driverNameEn,
        int boxesCount,
        CancellationToken cancellationToken = default)
    {
        var titleAr = "تم تسليم كل الطلبات 🎉";
        var titleEn = "All Orders Delivered";
        var bodyAr = string.IsNullOrWhiteSpace(driverNameAr)
            ? $"أنهت الرحلة {tripCode} توصيل كل الطلبات ({boxesCount}) بنجاح."
            : $"أنهى السائق {driverNameAr} توصيل كل الطلبات ({boxesCount}) للرحلة {tripCode} بنجاح.";
        var bodyEn = string.IsNullOrWhiteSpace(driverNameEn)
            ? $"Trip {tripCode} delivered all {boxesCount} orders successfully."
            : $"Driver {driverNameEn} delivered all {boxesCount} orders of trip {tripCode}.";

        await NotifyAsync(
            restaurantId, tripId, "trip-delivered",
            $"/restaurant/delivery/trips/{tripId}", "delivery/trips",
            titleAr, titleEn, bodyAr, bodyEn,
            new Dictionary<string, string>
            {
                ["eventKey"] = "driver.trip.delivered",
                ["tripId"] = tripId.ToString(),
                ["tripCode"] = tripCode,
                ["boxesCount"] = boxesCount.ToString()
            },
            cancellationToken);
    }

    private async Task NotifyAsync(
        Guid restaurantId,
        Guid tripId,
        string type,
        string deepLink,
        string section,
        string titleAr,
        string titleEn,
        string bodyAr,
        string bodyEn,
        Dictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        try
        {
            var userIds = await ResolveTargetUserIdsAsync(restaurantId, cancellationToken);
            if (userIds.Count == 0)
            {
                _logger?.LogInformation("No restaurant staff users resolved for {EventKey} of restaurant {RestaurantId}.", data["eventKey"], restaurantId);
                return;
            }

            foreach (var userId in userIds)
            {
                _db.RestaurantNotifications.Add(new RestaurantNotification
                {
                    UserId = userId,
                    Type = type,
                    TitleAr = titleAr,
                    TitleEn = titleEn,
                    BodyAr = bodyAr,
                    BodyEn = bodyEn,
                    Section = section,
                    DeepLink = deepLink,
                    SubjectId = tripId,
                    SentAtUtc = DateTime.UtcNow,
                    IsRead = false
                });
            }
            await _db.SaveChangesAsync(cancellationToken);

            foreach (var userId in userIds)
            {
                await _hubContext.Clients
                    .Group(RestaurantReviewHub.GroupForUser(userId.ToString()))
                    .SendAsync("notification-received", new { type }, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist restaurant delivery notification {EventKey}.", data["eventKey"]);
            return;
        }

        await PushFcmAsync(restaurantId, titleAr, bodyAr, data, cancellationToken);
    }

    private async Task<List<Guid>> ResolveTargetUserIdsAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        var userIds = new HashSet<Guid>();

        var ownerUserId = await _db.RestaurantProfiles
            .AsNoTracking()
            .Where(r => r.Id == restaurantId)
            .Select(r => r.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (ownerUserId != Guid.Empty)
        {
            userIds.Add(ownerUserId);
        }

        var dmUserIds = await _db.DeliveryManagerRegistrations
            .AsNoTracking()
            .Where(d => d.RestaurantId == restaurantId && d.ResultingUserId.HasValue && !d.IsDeleted)
            .Select(d => d.ResultingUserId!.Value)
            .ToListAsync(cancellationToken);
        foreach (var id in dmUserIds)
        {
            userIds.Add(id);
        }

        var scopedUserIds = await _db.UserScopes
            .AsNoTracking()
            .Where(x => x.AccessScope.RestaurantId == restaurantId && x.AccessScope.IsActive)
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken);
        foreach (var id in scopedUserIds)
        {
            userIds.Add(id);
        }

        return userIds.ToList();
    }

    private async Task PushFcmAsync(
        Guid restaurantId,
        string titleAr,
        string bodyAr,
        Dictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        if (_fcm is null || !_fcm.IsConfigured)
            return;

        List<string> tokens;
        try
        {
            tokens = await _db.RestaurantDeviceTokens.AsNoTracking()
                .Where(x => x.IsActive && x.RestaurantId == restaurantId)
                .Select(x => x.Token)
                .Distinct()
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load restaurant device tokens for {EventKey}.", data["eventKey"]);
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
                _logger?.LogWarning(ex, "Failed to send restaurant delivery push for {EventKey}.", data["eventKey"]);
            }
        }
    }
}
