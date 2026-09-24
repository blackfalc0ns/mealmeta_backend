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

    public async Task NotifyOrderSentToRestaurantAsync(
        Guid restaurantId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
    {
        var userIds = await _db.UserScopes
            .AsNoTracking()
            .Where(x => x.AccessScope.RestaurantId == restaurantId
                        && x.AccessScope.IsActive
                        && x.AccessScope.ScopeType == AccessScopeType.Restaurant)
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

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
                SubjectId = subscriptionDayId,
                SentAtUtc = DateTime.UtcNow
            });

            await _hubContext.Clients
                .Group(RestaurantReviewHub.GroupForUser(userId.ToString()))
                .SendAsync(
                    EventName,
                    new { subscriptionDayId, serviceDate = serviceDate.ToString("yyyy-MM-dd") },
                    cancellationToken);
        }
    }

    public async Task NotifyKitchenBatchReadyAsync(
        Guid restaurantId,
        Guid subscriptionDayId,
        int boxCount,
        CancellationToken cancellationToken = default)
    {
        if (_dispatcherHub != null)
        {
            var payload = new
            {
                restaurantId,
                subscriptionDayId,
                boxCount,
                readyAtUtc = DateTime.UtcNow
            };

            await _dispatcherHub.Clients.All.SendAsync("kitchen-batch-ready", payload, cancellationToken);
            await _dispatcherHub.Clients.Group(DispatcherHub.GroupForRestaurant(restaurantId))
                .SendAsync("kitchen-batch-ready", payload, cancellationToken);
        }
    }
}
