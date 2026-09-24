using MealMate.Application.Common.Abstractions;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Push;

public sealed class FirebaseDriverPushNotifier : IDriverPushNotifier
{
    private readonly IFcmMessagingClient _fcm;
    private readonly IAppDbContext _db;
    private readonly ILogger<FirebaseDriverPushNotifier> _logger;

    public FirebaseDriverPushNotifier(
        IFcmMessagingClient fcm,
        IAppDbContext db,
        ILogger<FirebaseDriverPushNotifier> logger)
    {
        _fcm = fcm;
        _db = db;
        _logger = logger;
    }

    public async Task NotifyBoxAssignedAsync(
        Guid driverUserId,
        string boxCode,
        string zoneName,
        CancellationToken cancellationToken = default)
    {
        if (driverUserId == Guid.Empty || !_fcm.IsConfigured)
            return;

        var tokens = await GetActiveDriverTokensAsync(driverUserId, cancellationToken);
        if (tokens.Count == 0)
            return;

        var title = "تم إسناد طلب جديد 📦";
        var body = $"تم إسناد الطلب {boxCode} في منطقة {zoneName} إليك للتوصيل.";
        var data = new Dictionary<string, string>
        {
            ["type"] = "box-assigned",
            ["boxCode"] = boxCode,
            ["zoneName"] = zoneName,
            ["action"] = $"/driver/orders/{boxCode}"
        };

        await BroadcastFcmAsync(tokens, title, body, data, cancellationToken);
    }

    public async Task NotifyTripAssignedAsync(
        Guid driverUserId,
        string tripCode,
        int boxesCount,
        CancellationToken cancellationToken = default)
    {
        if (driverUserId == Guid.Empty || !_fcm.IsConfigured)
            return;

        var tokens = await GetActiveDriverTokensAsync(driverUserId, cancellationToken);
        if (tokens.Count == 0)
            return;

        var title = "تم تعيين رحلة جديدة 🛵";
        var body = $"تم إسناد الرحلة {tripCode} بعدد {boxesCount} صناديق للوردية الحالية.";
        var data = new Dictionary<string, string>
        {
            ["type"] = "trip-assigned",
            ["tripCode"] = tripCode,
            ["boxesCount"] = boxesCount.ToString(),
            ["action"] = "/driver/pickup/manifest"
        };

        await BroadcastFcmAsync(tokens, title, body, data, cancellationToken);
    }

    public async Task NotifyKitchenReadyForPickupAsync(
        Guid driverUserId,
        string tripCode,
        CancellationToken cancellationToken = default)
    {
        if (driverUserId == Guid.Empty || !_fcm.IsConfigured)
            return;

        var tokens = await GetActiveDriverTokensAsync(driverUserId, cancellationToken);
        if (tokens.Count == 0)
            return;

        var title = "الشحنة جاهزة للاستلام بالمطبخ 🍳";
        var body = $"أنهى المطبخ تجهيز وجبات الرحلة {tripCode}. يمكنك التوجه للاستلام ومسح الباركود الآن.";
        var data = new Dictionary<string, string>
        {
            ["type"] = "kitchen-ready",
            ["tripCode"] = tripCode,
            ["action"] = "/driver/pickup/manifest"
        };

        await BroadcastFcmAsync(tokens, title, body, data, cancellationToken);
    }

    private async Task<List<string>> GetActiveDriverTokensAsync(Guid driverUserId, CancellationToken cancellationToken)
    {
        try
        {
            return await _db.DriverDeviceTokens
                .AsNoTracking()
                .Where(x => x.UserId == driverUserId && x.IsActive && !string.IsNullOrWhiteSpace(x.Token))
                .Select(x => x.Token)
                .Distinct()
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query DriverDeviceTokens for UserId: {UserId}", driverUserId);
            return [];
        }
    }

    private async Task BroadcastFcmAsync(
        List<string> tokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        foreach (var token in tokens)
        {
            try
            {
                var result = await _fcm.SendAsync(token, title, body, data, cancellationToken);
                if (!result.IsSuccess && result.IsInvalidToken)
                {
                    _logger.LogInformation("FCM token for driver is invalid or expired: {Token}", token);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send FCM push notification to token {Token}", token);
            }
        }
    }
}
