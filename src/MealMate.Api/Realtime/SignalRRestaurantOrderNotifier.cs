using MealMate.Application.Common.Abstractions;
using MealMate.Api.Resources;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace MealMate.Api.Realtime;

/// <summary>
/// Persists in-app notifications and pushes a realtime event to restaurant staff
/// when a subscription day order is dispatched at −72h.
/// </summary>
public sealed class SignalRRestaurantOrderNotifier : IRestaurantOrderNotifier
{
    internal const string EventName = "restaurant-order-sent";

    private readonly IHubContext<RestaurantReviewHub> _hubContext;
    private readonly IHubContext<DispatcherHub>? _dispatcherHub;
    private readonly IAppDbContext _db;
    private readonly IStringLocalizer<Messages> _messages;

    public SignalRRestaurantOrderNotifier(
        IHubContext<RestaurantReviewHub> hubContext,
        IAppDbContext db,
        IStringLocalizer<Messages> messages,
        IHubContext<DispatcherHub>? dispatcherHub = null)
    {
        _hubContext = hubContext;
        _db = db;
        _messages = messages;
        _dispatcherHub = dispatcherHub;
    }

    private async Task<List<Guid>> ResolveTargetUserIdsAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        var userIds = new HashSet<Guid>();

        // 1. Restaurant owner user id from RestaurantProfiles
        var ownerUserId = await _db.RestaurantProfiles
            .AsNoTracking()
            .Where(r => r.Id == restaurantId)
            .Select(r => r.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (ownerUserId != Guid.Empty)
        {
            userIds.Add(ownerUserId);
        }

        // 2. Delivery Managers from DeliveryManagerRegistrations
        var dmUserIds = await _db.DeliveryManagerRegistrations
            .AsNoTracking()
            .Where(d => d.RestaurantId == restaurantId && d.ResultingUserId.HasValue && !d.IsDeleted)
            .Select(d => d.ResultingUserId!.Value)
            .ToListAsync(cancellationToken);

        foreach (var id in dmUserIds)
        {
            userIds.Add(id);
        }

        // 3. Users linked via UserScopes
        var scopedUserIds = await _db.UserScopes
            .AsNoTracking()
            .Where(x => x.AccessScope.RestaurantId == restaurantId
                        && x.AccessScope.IsActive)
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken);

        foreach (var id in scopedUserIds)
        {
            userIds.Add(id);
        }

        return userIds.ToList();
    }

    public async Task NotifyOrderSentToRestaurantAsync(
        Guid restaurantId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
    {
        var userIds = await ResolveTargetUserIdsAsync(restaurantId, cancellationToken);
        if (userIds.Count == 0)
            return;

        var titleAr = _messages["notifications.order.sent.title_ar"].Value;
        var titleEn = _messages["notifications.order.sent.title_en"].Value;
        var bodyAr = string.Format(_messages["notifications.order.sent.body_ar"].Value, serviceDate.ToString("yyyy-MM-dd"));
        var bodyEn = string.Format(_messages["notifications.order.sent.body_en"].Value, serviceDate.ToString("yyyy-MM-dd"));

        foreach (var userId in userIds)
        {
            _db.RestaurantNotifications.Add(new RestaurantNotification
            {
                UserId = userId,
                Type = "order-sent",
                TitleAr = titleAr,
                TitleEn = titleEn,
                BodyAr = bodyAr,
                BodyEn = bodyEn,
                Section = "orders/pending-confirmation",
                DeepLink = "/restaurant/orders/pending-confirmation",
                SubjectId = subscriptionDayId,
                SentAtUtc = DateTime.UtcNow,
                IsRead = false
            });

            await _hubContext.Clients
                .Group(RestaurantReviewHub.GroupForUser(userId.ToString()))
                .SendAsync(
                    EventName,
                    new { subscriptionDayId, serviceDate = serviceDate.ToString("yyyy-MM-dd") },
                    cancellationToken);

            await _hubContext.Clients
                .Group(RestaurantReviewHub.GroupForUser(userId.ToString()))
                .SendAsync(
                    "registration-review-updated",
                    new { registrationId = restaurantId, changeType = "order-sent" },
                    cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task NotifyKitchenBatchReadyAsync(
        Guid restaurantId,
        Guid subscriptionDayId,
        int boxCount,
        CancellationToken cancellationToken = default)
    {
        var userIds = await ResolveTargetUserIdsAsync(restaurantId, cancellationToken);

        var day = await _db.SubscriptionDays
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == subscriptionDayId, cancellationToken);

        var orderCode = day != null
            ? $"ORD-{day.ServiceDate:MMdd}-{day.Id.ToString()[..4].ToUpperInvariant()}"
            : $"BOX-{subscriptionDayId.ToString()[..4].ToUpperInvariant()}";

        var titleAr = $"📦 طلب جاهز للتسليم ({orderCode})";
        var titleEn = $"📦 Order Ready for Delivery ({orderCode})";
        var bodyAr = $"تم تجهيز وطباعة الطلب {orderCode} بنجاح، وأصبح جاهزاً لاستلام وتوزيع مسؤول التوصيل.";
        var bodyEn = $"Order {orderCode} has been packed and printed, and is ready for delivery manager dispatch.";

        foreach (var userId in userIds)
        {
            _db.RestaurantNotifications.Add(new RestaurantNotification
            {
                UserId = userId,
                Type = "kitchen-order-ready",
                TitleAr = titleAr,
                TitleEn = titleEn,
                BodyAr = bodyAr,
                BodyEn = bodyEn,
                Section = "orders/handover",
                DeepLink = "/restaurant/orders/handover",
                SubjectId = subscriptionDayId,
                SentAtUtc = DateTime.UtcNow,
                IsRead = false
            });

            await _hubContext.Clients
                .Group(RestaurantReviewHub.GroupForUser(userId.ToString()))
                .SendAsync(
                    "registration-review-updated",
                    new { registrationId = restaurantId, changeType = "kitchen-order-ready" },
                    cancellationToken);

            await _hubContext.Clients
                .Group(RestaurantReviewHub.GroupForUser(userId.ToString()))
                .SendAsync(
                    "notification-received",
                    new { type = "kitchen-order-ready", title = titleAr, orderCode, subscriptionDayId },
                    cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        if (_dispatcherHub != null)
        {
            var payload = new
            {
                restaurantId,
                subscriptionDayId,
                orderCode,
                boxCount,
                readyAtUtc = DateTime.UtcNow
            };

            await _dispatcherHub.Clients.All.SendAsync("kitchen-batch-ready", payload, cancellationToken);
            await _dispatcherHub.Clients.Group(DispatcherHub.GroupForRestaurant(restaurantId))
                .SendAsync("kitchen-batch-ready", payload, cancellationToken);
            await _dispatcherHub.Clients.All.SendAsync("kitchen-orders-ready", payload, cancellationToken);
            await _dispatcherHub.Clients.Group(DispatcherHub.GroupForRestaurant(restaurantId))
                .SendAsync("kitchen-orders-ready", payload, cancellationToken);
        }
    }
}
