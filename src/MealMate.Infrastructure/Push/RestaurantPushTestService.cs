using MealMate.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Push;

public sealed class RestaurantPushTestService : IRestaurantPushTestService
{
    private readonly IAppDbContext _db;
    private readonly IFcmMessagingClient _fcm;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<RestaurantPushTestService> _logger;

    public RestaurantPushTestService(
        IAppDbContext db,
        IFcmMessagingClient fcm,
        IDateTimeProvider clock,
        ILogger<RestaurantPushTestService> logger)
    {
        _db = db;
        _fcm = fcm;
        _clock = clock;
        _logger = logger;
    }

    public async Task<RestaurantPushTestResultDto> SendTestAsync(
        Guid? restaurantUserId,
        string? directToken = null,
        CancellationToken cancellationToken = default)
    {
        if (!_fcm.IsConfigured)
        {
            return new RestaurantPushTestResultDto(
                false,
                0,
                0,
                0,
                ["Firebase credentials are not configured on server."]);
        }

        var tokensToSend = new List<string>();

        if (!string.IsNullOrWhiteSpace(directToken))
        {
            tokensToSend.Add(directToken.Trim());
        }
        else if (restaurantUserId.HasValue)
        {
            tokensToSend = await _db.RestaurantDeviceTokens
                .Where(x => x.UserId == restaurantUserId.Value && x.IsActive)
                .Select(x => x.Token)
                .ToListAsync(cancellationToken);
        }

        if (tokensToSend.Count == 0)
        {
            return new RestaurantPushTestResultDto(
                true,
                0,
                0,
                0,
                ["No active browser tokens found. Please grant notification permission in Restaurant Dashboard first."]);
        }

        var sent = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var token in tokensToSend)
        {
            var result = await _fcm.SendAsync(
                token,
                "إشعار تجريبي للوحة المطعم 🍽️",
                "تهانينا! تم تفعيل إشعارات الويب للوحة المطعم بنجاح.",
                new Dictionary<string, string>
                {
                    ["eventKey"] = "restaurant.test_push",
                    ["deepLink"] = "/restaurant",
                },
                cancellationToken);

            if (result.IsSuccess)
            {
                sent++;
            }
            else
            {
                failed++;
                if (result.ErrorMessage is not null)
                    errors.Add(result.ErrorMessage);
            }
        }

        _logger.LogInformation(
            "Restaurant test push completed for {UserId}: {Sent} sent, {Failed} failed out of {Total} tokens.",
            restaurantUserId, sent, failed, tokensToSend.Count);

        return new RestaurantPushTestResultDto(
            true,
            tokensToSend.Count,
            sent,
            failed,
            errors);
    }

    public async Task<TestPushDeliveryResultDto> SendTestToDispatcherAsync(
        Guid restaurantId,
        Guid? deliveryManagerId = null,
        string? customMessage = null,
        CancellationToken cancellationToken = default)
    {
        if (!_fcm.IsConfigured)
        {
            return new TestPushDeliveryResultDto(
                false,
                "Dispatcher",
                0,
                "خدمة إشعارات Firebase غير مفعلة على الخادم.",
                ["FCM is not configured."]);
        }

        var restaurant = await _db.RestaurantProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == restaurantId, cancellationToken);

        var restaurantName = restaurant?.TradeNameAr ?? restaurant?.TradeNameEn ?? "المطعم";

        var allTargetUserIds = new HashSet<Guid>();

        if (deliveryManagerId.HasValue && deliveryManagerId.Value != Guid.Empty)
        {
            var dm = await _db.DeliveryManagerRegistrations
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == deliveryManagerId.Value, cancellationToken);

            var dmUserId = dm?.ResultingUserId ?? dm?.SubmittedByUserId;
            if (dmUserId.HasValue)
            {
                allTargetUserIds.Add(dmUserId.Value);
            }
            else
            {
                allTargetUserIds.Add(deliveryManagerId.Value);
            }
        }
        else
        {
            var dmRegistrations = await _db.DeliveryManagerRegistrations
                .AsNoTracking()
                .Where(d => d.RestaurantId == restaurantId)
                .Select(d => new { d.ResultingUserId, d.SubmittedByUserId })
                .ToListAsync(cancellationToken);

            var dmUserIds = dmRegistrations
                .Select(d => d.ResultingUserId ?? d.SubmittedByUserId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            var scopedUserIds = await _db.UserScopes
                .AsNoTracking()
                .Where(s => s.AccessScope.RestaurantId == restaurantId && s.AccessScope.IsActive)
                .Select(s => s.UserId)
                .ToListAsync(cancellationToken);

            foreach (var uid in dmUserIds.Concat(scopedUserIds))
            {
                allTargetUserIds.Add(uid);
            }
        }

        var tokens = await _db.RestaurantDeviceTokens
            .AsNoTracking()
            .Where(t => t.IsActive && !string.IsNullOrWhiteSpace(t.Token) &&
                        (t.RestaurantId == restaurantId || (allTargetUserIds.Count > 0 && allTargetUserIds.Contains(t.UserId))))
            .Select(t => t.Token)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0)
        {
            return new TestPushDeliveryResultDto(
                false,
                "Dispatcher",
                0,
                "لم يتم العثور على أجهزة مسجلة ونشطة لمسؤول التوصيل في هذا المطعم حالياً. تأكد من فتح التطبيق وتسجيل التوكن.",
                ["No active tokens found for Delivery Manager."]);
        }

        var title = $"🔔 إشعار تجريبي لمسؤول التوصيل · {restaurantName}";
        var body = !string.IsNullOrWhiteSpace(customMessage)
            ? customMessage.Trim()
            : "هذا إشعار تجريبي لاختبار اتصال واستقبال إشعارات الموبايل لمسؤولي التوصيل بنجاح.";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "dispatcher.test_push",
            ["type"] = "test-push",
            ["restaurantId"] = restaurantId.ToString(),
            ["action"] = "/dispatcher/orders/queue"
        };

        var sentCount = 0;
        var errors = new List<string>();

        foreach (var token in tokens)
        {
            try
            {
                var sendResult = await _fcm.SendAsync(token, title, body, data, cancellationToken);
                if (sendResult.IsSuccess)
                {
                    sentCount++;
                }
                else
                {
                    if (sendResult.ErrorMessage != null) errors.Add(sendResult.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send test push to token {Token}", token);
                errors.Add(ex.Message);
            }
        }

        return new TestPushDeliveryResultDto(
            sentCount > 0,
            "Dispatcher",
            sentCount,
            sentCount > 0
                ? $"تم إرسال الإشعار التجريبي بنجاح إلى {sentCount} جهاز لمسؤول التوصيل."
                : "فشل إرسال الإشعار إلى الأجهزة المسجلة.",
            errors.Count > 0 ? errors : null);
    }

    public async Task<TestPushDeliveryResultDto> SendTestToDriverAsync(
        Guid restaurantId,
        Guid? driverId = null,
        string? customMessage = null,
        CancellationToken cancellationToken = default)
    {
        if (!_fcm.IsConfigured)
        {
            return new TestPushDeliveryResultDto(
                false,
                "Driver",
                0,
                "خدمة إشعارات Firebase غير مفعلة على الخادم.",
                ["FCM is not configured."]);
        }

        var restaurant = await _db.RestaurantProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == restaurantId, cancellationToken);

        var restaurantName = restaurant?.TradeNameAr ?? restaurant?.TradeNameEn ?? "المطعم";

        var targetDriverUserIds = new HashSet<Guid>();

        if (driverId.HasValue && driverId.Value != Guid.Empty)
        {
            var driverProfile = await _db.DriverProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == driverId.Value, cancellationToken);

            if (driverProfile is not null)
            {
                targetDriverUserIds.Add(driverProfile.UserId);
            }
            else
            {
                var reg = await _db.DriverRegistrations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == driverId.Value, cancellationToken);

                if (reg?.SubmittedByUserId.HasValue == true)
                {
                    targetDriverUserIds.Add(reg.SubmittedByUserId.Value);
                }
                else
                {
                    targetDriverUserIds.Add(driverId.Value);
                }
            }
        }
        else
        {
            var profileUserIds = await _db.DriverProfiles
                .AsNoTracking()
                .Where(d => d.RestaurantId == restaurantId)
                .Select(d => d.UserId)
                .ToListAsync(cancellationToken);

            var regUserIds = await _db.DriverRegistrations
                .AsNoTracking()
                .Where(r => r.RestaurantId == restaurantId && r.SubmittedByUserId != null)
                .Select(r => r.SubmittedByUserId!.Value)
                .ToListAsync(cancellationToken);

            foreach (var uid in profileUserIds.Concat(regUserIds))
            {
                targetDriverUserIds.Add(uid);
            }
        }

        var tokens = await _db.DriverDeviceTokens
            .AsNoTracking()
            .Where(t => t.IsActive && !string.IsNullOrWhiteSpace(t.Token) &&
                        targetDriverUserIds.Contains(t.UserId))
            .Select(t => t.Token)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0)
        {
            return new TestPushDeliveryResultDto(
                false,
                "Driver",
                0,
                "لم يتم العثور على أجهزة مسجلة ونشطة للسائقين في هذا المطعم حالياً. تأكد من فتح التطبيق وتسجيل التوكن.",
                ["No active tokens found for Drivers."]);
        }

        var title = $"🛵 إشعار تجريبي للسائق · {restaurantName}";
        var body = !string.IsNullOrWhiteSpace(customMessage)
            ? customMessage.Trim()
            : "هذا إشعار تجريبي لاختبار اتصال واستقبال إشعارات الموبايل لأسطول السائقين بنجاح.";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "driver.test_push",
            ["type"] = "test-push",
            ["restaurantId"] = restaurantId.ToString(),
            ["action"] = "/driver/home"
        };

        var sentCount = 0;
        var errors = new List<string>();

        foreach (var token in tokens)
        {
            try
            {
                var sendResult = await _fcm.SendAsync(token, title, body, data, cancellationToken);
                if (sendResult.IsSuccess)
                {
                    sentCount++;
                }
                else
                {
                    if (sendResult.ErrorMessage != null) errors.Add(sendResult.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send test push to token {Token}", token);
                errors.Add(ex.Message);
            }
        }

        return new TestPushDeliveryResultDto(
            sentCount > 0,
            "Driver",
            sentCount,
            sentCount > 0
                ? $"تم إرسال الإشعار التجريبي بنجاح إلى {sentCount} جهاز للسائقين."
                : "فشل إرسال الإشعار إلى الأجهزة المسجلة.",
            errors.Count > 0 ? errors : null);
    }
}
