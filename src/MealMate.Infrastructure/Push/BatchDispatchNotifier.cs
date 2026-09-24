using System.Collections.Concurrent;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Push;

/// <summary>
/// Domain-driven service that throttles kitchen-ready alerts to avoid spamming the delivery manager (dispatcher).
/// Aggregates notifications into batches of 5 ready orders, enforces a 3-minute cooldown between bursts,
/// and debounces hanging orders (> 10 minutes) so deliveries are never stranded.
/// </summary>
public sealed class BatchDispatchNotifier : IBatchDispatchNotifier
{
    public const int DefaultBatchThreshold = 5;
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan Debounce = TimeSpan.FromMinutes(10);

    private readonly IAppDbContext _db;
    private readonly IFcmMessagingClient _fcm;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<BatchDispatchNotifier> _logger;
    private readonly IRestaurantOrderNotifier? _restaurantNotifier;

    private sealed class RestaurantBatchState
    {
        public int LastNotifiedCount { get; set; }
        public DateTime LastNotifiedAtUtc { get; set; }
    }

    private static readonly ConcurrentDictionary<Guid, RestaurantBatchState> States = new();

    public BatchDispatchNotifier(
        IAppDbContext db,
        IFcmMessagingClient fcm,
        IDateTimeProvider clock,
        ILogger<BatchDispatchNotifier> logger,
        IRestaurantOrderNotifier? restaurantNotifier = null)
    {
        _db = db;
        _fcm = fcm;
        _clock = clock;
        _logger = logger;
        _restaurantNotifier = restaurantNotifier;
    }

    public async Task NotifyIfBatchThresholdReachedAsync(
        Guid restaurantId,
        Guid subscriptionDayId,
        CancellationToken cancellationToken = default)
    {
        var readyCount = await GetUnassignedReadyCountAsync(restaurantId, cancellationToken);
        var now = _clock.UtcNow;
        var state = States.GetOrAdd(restaurantId, _ => new RestaurantBatchState());

        lock (state)
        {
            if (readyCount < state.LastNotifiedCount)
            {
                // Previously notified orders have been assigned to drivers
                state.LastNotifiedCount = 0;
            }

            var countReachedThreshold = readyCount >= DefaultBatchThreshold
                && readyCount >= state.LastNotifiedCount + DefaultBatchThreshold;

            if (!countReachedThreshold)
            {
                _logger.LogDebug(
                    "Kitchen ready batch threshold not reached for restaurant {RestaurantId}. Current: {Current}, LastNotified: {LastNotified}",
                    restaurantId, readyCount, state.LastNotifiedCount);
                return;
            }

            var cooldownElapsed = state.LastNotifiedAtUtc == default
                || (now - state.LastNotifiedAtUtc) >= Cooldown;

            if (!cooldownElapsed)
            {
                _logger.LogInformation(
                    "Kitchen ready batch notification throttled by cooldown for restaurant {RestaurantId}. ReadyCount: {Count}",
                    restaurantId, readyCount);
                return;
            }

            state.LastNotifiedCount = readyCount;
            state.LastNotifiedAtUtc = now;
        }

        await SendBatchNotificationAsync(restaurantId, readyCount, isHanging: false, subscriptionDayId, cancellationToken);
    }

    public async Task CheckAndNotifyHangingBatchesAsync(
        Guid restaurantId,
        CancellationToken cancellationToken = default)
    {
        var readyCount = await GetUnassignedReadyCountAsync(restaurantId, cancellationToken);
        var now = _clock.UtcNow;
        var state = States.GetOrAdd(restaurantId, _ => new RestaurantBatchState());

        lock (state)
        {
            if (readyCount <= 0 || readyCount <= state.LastNotifiedCount)
            {
                return;
            }

            var debounceElapsed = state.LastNotifiedAtUtc == default
                || (now - state.LastNotifiedAtUtc) >= Debounce;

            if (!debounceElapsed)
            {
                return;
            }

            state.LastNotifiedCount = readyCount;
            state.LastNotifiedAtUtc = now;
        }

        await SendBatchNotificationAsync(restaurantId, readyCount, isHanging: true, Guid.Empty, cancellationToken);
    }

    private async Task<int> GetUnassignedReadyCountAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        var assignedDayIds = _db.TripStops
            .AsNoTracking()
            .Where(s => s.SubscriptionDayId.HasValue)
            .Select(s => s.SubscriptionDayId!.Value);

        return await _db.RestaurantDailyOrders
            .AsNoTracking()
            .Where(o => o.RestaurantId == restaurantId
                     && o.Status == OrderStatus.ReadyForPickup
                     && !assignedDayIds.Contains(o.SubscriptionDayId))
            .CountAsync(cancellationToken);
    }

    private async Task SendBatchNotificationAsync(
        Guid restaurantId,
        int readyCount,
        bool isHanging,
        Guid subscriptionDayId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_restaurantNotifier != null && subscriptionDayId != Guid.Empty)
            {
                await _restaurantNotifier.NotifyKitchenBatchReadyAsync(
                    restaurantId,
                    subscriptionDayId,
                    readyCount,
                    cancellationToken);
            }

            if (!_fcm.IsConfigured)
            {
                _logger.LogInformation("FCM not configured; skipped dispatch batch push notification for restaurant {RestaurantId}", restaurantId);
                return;
            }

            var restaurantUserIds = await _db.UserScopes
                .AsNoTracking()
                .Where(s => s.AccessScope.RestaurantId == restaurantId && s.AccessScope.IsActive)
                .Select(s => s.UserId)
                .ToListAsync(cancellationToken);

            var tokens = await _db.RestaurantDeviceTokens
                .AsNoTracking()
                .Where(t => t.IsActive && (t.RestaurantId == restaurantId || restaurantUserIds.Contains(t.UserId)))
                .Select(t => t.Token)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (tokens.Count == 0)
            {
                _logger.LogInformation("No active device tokens found for restaurant {RestaurantId} dispatcher notification", restaurantId);
                return;
            }

            var titleAr = isHanging
                ? "📦 طلبات جاهزة للتوزيع بالمطبخ"
                : "📦 دفعة طلبات جاهزة للتوزيع";
            var titleEn = isHanging
                ? "Orders Ready for Dispatch"
                : "Ready Orders Batch Available";
            var bodyAr = $"يتوفر الآن {readyCount} طلبات جاهزة بالمطبخ بانتظار الإسناد للسائقين.";
            var bodyEn = $"{readyCount} orders are now packed and ready for driver dispatch.";

            var data = new Dictionary<string, string>
            {
                ["eventKey"] = "dispatcher.batch.ready",
                ["restaurantId"] = restaurantId.ToString(),
                ["readyCount"] = readyCount.ToString(),
                ["action"] = "/dispatcher/orders/queue"
            };

            var sendTasks = tokens.Select(token => _fcm.SendAsync(
                token,
                titleAr,
                bodyAr,
                data,
                cancellationToken));

            await Task.WhenAll(sendTasks);

            _logger.LogInformation(
                "Dispatched batch ready push notification to {TokenCount} devices for restaurant {RestaurantId} (Count: {ReadyCount}, Hanging: {IsHanging})",
                tokens.Count, restaurantId, readyCount, isHanging);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send batch ready push notification for restaurant {RestaurantId}", restaurantId);
        }
    }
}
