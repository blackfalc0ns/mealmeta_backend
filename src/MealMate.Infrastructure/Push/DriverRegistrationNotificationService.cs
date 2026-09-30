using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Push;

public sealed class DriverRegistrationNotificationService : IDriverRegistrationNotificationService
{
    private readonly IAppDbContext _db;
    private readonly IFcmMessagingClient _fcm;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<DriverRegistrationNotificationService> _logger;
    private readonly ILocalizedMessageProvider _messages;

    public DriverRegistrationNotificationService(
        IAppDbContext db,
        IFcmMessagingClient fcm,
        IDateTimeProvider clock,
        ILogger<DriverRegistrationNotificationService> logger,
        ILocalizedMessageProvider? messages = null)
    {
        _db = db;
        _fcm = fcm;
        _clock = clock;
        _logger = logger;
        _messages = messages ?? NullLocalizedMessageProvider.Instance;
    }

    public async Task NotifyRestaurantDriverApplicationSubmittedAsync(
        Guid restaurantId,
        Guid registrationId,
        string driverName,
        string phone,
        CancellationToken cancellationToken = default)
    {
        var restaurant = await _db.RestaurantProfiles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == restaurantId, cancellationToken);

        if (restaurant is null)
        {
            _logger.LogWarning("Cannot notify restaurant for driver registration {RegistrationId}. Restaurant {RestaurantId} not found.", registrationId, restaurantId);
            return;
        }

        var titleAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationRestaurantSubmittedTitle);
        var titleEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationRestaurantSubmittedTitle);
        var bodyAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationRestaurantSubmittedBody, arguments: [driverName, phone]);
        var bodyEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationRestaurantSubmittedBody, arguments: [driverName, phone]);
        var deepLink = $"/restaurant/drivers/registrations/{registrationId}";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "driver.registration.submitted",
            ["registrationId"] = registrationId.ToString(),
            ["restaurantId"] = restaurantId.ToString(),
            ["driverName"] = driverName,
            ["phone"] = phone,
            ["deepLink"] = deepLink,
        };

        // 1. Persist in-app notification for the restaurant
        try
        {
            var alreadyExists = await _db.RestaurantNotifications.AnyAsync(
                n => n.UserId == restaurant.UserId &&
                     n.SubjectId == registrationId &&
                     n.Type == "driver.registration.submitted", cancellationToken);

            if (!alreadyExists)
            {
                _db.RestaurantNotifications.Add(new RestaurantNotification
                {
                    UserId = restaurant.UserId,
                    Type = "driver.registration.submitted",
                    TitleAr = titleAr,
                    TitleEn = titleEn,
                    BodyAr = bodyAr,
                    BodyEn = bodyEn,
                    Section = "drivers",
                    SubjectId = registrationId,
                    DeepLink = deepLink,
                    IsRead = false,
                    SentAtUtc = _clock.UtcNow,
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist restaurant notification for driver registration {RegistrationId}.", registrationId);
            throw;
        }

        // 2. Deliver real-time push to restaurant device tokens
        await SendRestaurantPushAsync(restaurantId, restaurant.UserId, titleAr, bodyAr, data, cancellationToken);
    }

    public async Task NotifyRestaurantDriverApplicationResubmittedAsync(
        Guid restaurantId,
        Guid registrationId,
        string driverName,
        string phone,
        CancellationToken cancellationToken = default)
    {
        var restaurant = await _db.RestaurantProfiles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == restaurantId, cancellationToken);

        if (restaurant is null)
        {
            _logger.LogWarning("Cannot notify restaurant for driver resubmission {RegistrationId}. Restaurant {RestaurantId} not found.", registrationId, restaurantId);
            return;
        }

        var titleAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationRestaurantResubmittedTitle);
        var titleEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationRestaurantResubmittedTitle);
        var bodyAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationRestaurantResubmittedBody, arguments: [driverName, phone]);
        var bodyEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationRestaurantResubmittedBody, arguments: [driverName, phone]);
        var deepLink = $"/restaurant/drivers/registrations/{registrationId}";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "driver.registration.resubmitted",
            ["registrationId"] = registrationId.ToString(),
            ["restaurantId"] = restaurantId.ToString(),
            ["driverName"] = driverName,
            ["phone"] = phone,
            ["deepLink"] = deepLink,
        };

        try
        {
            var alreadyExists = await _db.RestaurantNotifications.AnyAsync(
                n => n.UserId == restaurant.UserId &&
                     n.SubjectId == registrationId &&
                     n.Type == "driver.registration.resubmitted", cancellationToken);

            if (!alreadyExists)
            {
                _db.RestaurantNotifications.Add(new RestaurantNotification
                {
                    UserId = restaurant.UserId,
                    Type = "driver.registration.resubmitted",
                    TitleAr = titleAr,
                    TitleEn = titleEn,
                    BodyAr = bodyAr,
                    BodyEn = bodyEn,
                    Section = "drivers",
                    SubjectId = registrationId,
                    DeepLink = deepLink,
                    IsRead = false,
                    SentAtUtc = _clock.UtcNow,
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist restaurant resubmission notification for driver registration {RegistrationId}.", registrationId);
            throw;
        }

        await SendRestaurantPushAsync(restaurantId, restaurant.UserId, titleAr, bodyAr, data, cancellationToken);
    }

    public async Task NotifyAdminsDriverApplicationReadyForConfirmationAsync(
        Guid registrationId,
        Guid restaurantId,
        string restaurantName,
        string driverName,
        string phone,
        CancellationToken cancellationToken = default)
    {
        var isIndependent = restaurantId == Guid.Empty;
        var eventKey = isIndependent ? "driver.registration.independent_submitted" : "driver.registration.restaurant_approved";

        var titleAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationAdminReadyTitle);
        var titleEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationAdminReadyTitle);
        var bodyAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationAdminReadyBody, arguments: [restaurantName, driverName, phone]);
        var bodyEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationAdminReadyBody, arguments: [restaurantName, driverName, phone]);
        var deepLink = $"/admin/accounts/pending/driver/{registrationId}";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = eventKey,
            ["registrationId"] = registrationId.ToString(),
            ["restaurantId"] = restaurantId.ToString(),
            ["restaurantName"] = restaurantName,
            ["driverName"] = driverName,
            ["phone"] = phone,
            ["deepLink"] = deepLink,
            ["stage"] = "2",
            ["reviewStage"] = "2"
        };

        // 1. Persist in-app notification for admins
        try
        {
            var existingPayloads = await _db.AdminNotifications
                .Where(n => n.PayloadJson != null)
                .Select(n => n.PayloadJson!)
                .ToListAsync(cancellationToken);
            var registrationIdText = registrationId.ToString();
            var alreadyExists = existingPayloads.Any(payload =>
            {
                try
                {
                    using var document = JsonDocument.Parse(payload);
                    var root = document.RootElement;
                    return root.TryGetProperty("registrationId", out var storedRegistrationId)
                        && root.TryGetProperty("eventKey", out var storedEventKey)
                        && storedRegistrationId.GetString() == registrationIdText
                        && storedEventKey.GetString() == eventKey;
                }
                catch (JsonException)
                {
                    return false;
                }
            });

            if (!alreadyExists)
            {
                _db.AdminNotifications.Add(new AdminNotification
                {
                    UserId = null, // Visible to all admins
                    TitleAr = titleAr,
                    TitleEn = titleEn,
                    BodyAr = bodyAr,
                    BodyEn = bodyEn,
                    Channel = NotificationChannel.InApp,
                    IsRead = false,
                    SentAtUtc = _clock.UtcNow,
                    PayloadJson = JsonSerializer.Serialize(data),
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist admin notification for approved driver registration {RegistrationId}.", registrationId);
            throw;
        }

        // 2. Deliver real-time push to admin device tokens
        if (!_fcm.IsConfigured)
        {
            _logger.LogWarning("Firebase FCM is not configured. Admin push notification for driver registration {RegistrationId} skipped.", registrationId);
            return;
        }

        try
        {
            var adminTokens = await _db.AdminDeviceTokens
                .Where(x => x.IsActive)
                .Select(x => x.Token)
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var token in adminTokens)
            {
                try
                {
                    await _fcm.SendAsync(token, titleAr, bodyAr, data, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send admin push for driver registration {RegistrationId}.", registrationId);
                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query admin device tokens for driver registration {RegistrationId}.", registrationId);
            throw;
        }
    }

    private async Task SendRestaurantPushAsync(
        Guid restaurantId,
        Guid ownerUserId,
        string title,
        string body,
        Dictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        if (!_fcm.IsConfigured) return;

        try
        {
            var tokens = await _db.RestaurantDeviceTokens
                .Where(x => x.IsActive && (x.RestaurantId == restaurantId || x.UserId == ownerUserId))
                .Select(x => x.Token)
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var token in tokens)
            {
                try
                {
                    await _fcm.SendAsync(token, title, body, data, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send restaurant driver push to user {UserId}.", ownerUserId);
                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query restaurant device tokens for restaurant {RestaurantId}.", restaurantId);
            throw;
        }
    }

    public async Task NotifyDriverApplicationSubmittedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string restaurantName,
        Guid? restaurantId = null,
        int? reviewStage = null,
        string? applicationStatus = null,
        string? eventKey = null,
        CancellationToken cancellationToken = default)
    {
        var isIndependent = (!restaurantId.HasValue || restaurantId.Value == Guid.Empty) && (string.IsNullOrWhiteSpace(restaurantName) || restaurantName is "MealMate Fleet" or "MealMate");
        var stage = reviewStage?.ToString() ?? (isIndependent ? "2" : "1");
        var resolvedEventKey = eventKey ?? (isIndependent && reviewStage == 2 ? "driver.registration.independent_submitted" : "driver.registration.submitted");
        var status = applicationStatus ?? "Submitted";

        var titleAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationDriverSubmittedTitle);
        var titleEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationDriverSubmittedTitle);
        var bodyAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationDriverSubmittedBody, arguments: restaurantName);
        var bodyEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationDriverSubmittedBody, arguments: restaurantName);

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = resolvedEventKey,
            ["registrationId"] = registrationId.ToString(),
            ["restaurantId"] = restaurantId?.ToString() ?? string.Empty,
            ["stage"] = stage,
            ["reviewStage"] = stage,
            ["status"] = status,
            ["restaurantName"] = restaurantName,
        };

        await SendDriverPushAsync(driverUserId, phone, titleAr, titleEn, bodyAr, bodyEn, data, cancellationToken);
    }

    public async Task NotifyDriverRestaurantApprovedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string restaurantName,
        CancellationToken cancellationToken = default)
    {
        var titleAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationDriverRestaurantApprovedTitle);
        var titleEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationDriverRestaurantApprovedTitle);
        var bodyAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationDriverRestaurantApprovedBody, arguments: restaurantName);
        var bodyEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationDriverRestaurantApprovedBody, arguments: restaurantName);

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "driver.registration.restaurant_approved",
            ["registrationId"] = registrationId.ToString(),
            ["stage"] = "2",
            ["status"] = "UnderReview",
            ["restaurantName"] = restaurantName,
        };

        await SendDriverPushAsync(driverUserId, phone, titleAr, titleEn, bodyAr, bodyEn, data, cancellationToken);
    }

    public async Task NotifyDriverRestaurantChangesRequestedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string restaurantName,
        string notes,
        CancellationToken cancellationToken = default)
    {
        var titleAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationDriverRestaurantChangesTitle);
        var titleEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationDriverRestaurantChangesTitle);
        var bodyAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationDriverRestaurantChangesBody, arguments: [restaurantName, notes]);
        var bodyEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationDriverRestaurantChangesBody, arguments: [restaurantName, notes]);

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "driver.registration.restaurant_changes_requested",
            ["registrationId"] = registrationId.ToString(),
            ["stage"] = "2",
            ["status"] = "NeedsChanges",
            ["notes"] = notes,
            ["restaurantName"] = restaurantName,
        };

        await SendDriverPushAsync(driverUserId, phone, titleAr, titleEn, bodyAr, bodyEn, data, cancellationToken);
    }

    public async Task NotifyDriverRestaurantRejectedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string restaurantName,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var titleAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationDriverRestaurantRejectedTitle);
        var titleEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationDriverRestaurantRejectedTitle);
        var bodyAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationDriverRestaurantRejectedBody, arguments: [restaurantName, reason]);
        var bodyEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationDriverRestaurantRejectedBody, arguments: [restaurantName, reason]);

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "driver.registration.restaurant_rejected",
            ["registrationId"] = registrationId.ToString(),
            ["stage"] = "3",
            ["status"] = "Rejected",
            ["reason"] = reason,
            ["restaurantName"] = restaurantName,
        };

        await SendDriverPushAsync(driverUserId, phone, titleAr, titleEn, bodyAr, bodyEn, data, cancellationToken);
    }

    public async Task NotifyDriverAdminApprovedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string restaurantName,
        CancellationToken cancellationToken = default)
    {
        var titleAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationDriverAdminApprovedTitle);
        var titleEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationDriverAdminApprovedTitle);
        var bodyAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationDriverAdminApprovedBody, arguments: restaurantName);
        var bodyEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationDriverAdminApprovedBody, arguments: restaurantName);

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "driver.registration.admin_approved",
            ["registrationId"] = registrationId.ToString(),
            ["stage"] = "4",
            ["status"] = "Approved",
            ["restaurantName"] = restaurantName,
        };

        await SendDriverPushAsync(driverUserId, phone, titleAr, titleEn, bodyAr, bodyEn, data, cancellationToken);
    }

    public async Task NotifyDriverAdminChangesRequestedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string notes,
        CancellationToken cancellationToken = default)
    {
        var titleAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationDriverAdminChangesTitle);
        var titleEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationDriverAdminChangesTitle);
        var bodyAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationDriverAdminChangesBody, arguments: notes);
        var bodyEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationDriverAdminChangesBody, arguments: notes);

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "driver.registration.admin_changes_requested",
            ["registrationId"] = registrationId.ToString(),
            ["stage"] = "2",
            ["status"] = "NeedsChanges",
            ["notes"] = notes,
        };

        await SendDriverPushAsync(driverUserId, phone, titleAr, titleEn, bodyAr, bodyEn, data, cancellationToken);
    }

    public async Task NotifyDriverAdminRejectedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var titleAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationDriverAdminRejectedTitle);
        var titleEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationDriverAdminRejectedTitle);
        var bodyAr = _messages.GetArabicMessage(MessageCodes.Driver.NotificationDriverAdminRejectedBody, arguments: reason);
        var bodyEn = _messages.GetEnglishMessage(MessageCodes.Driver.NotificationDriverAdminRejectedBody, arguments: reason);

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "driver.registration.admin_rejected",
            ["registrationId"] = registrationId.ToString(),
            ["stage"] = "3",
            ["status"] = "Rejected",
            ["reason"] = reason,
        };

        await SendDriverPushAsync(driverUserId, phone, titleAr, titleEn, bodyAr, bodyEn, data, cancellationToken);
    }

    private async Task SendDriverPushAsync(
        Guid? driverUserId,
        string phone,
        string titleAr,
        string titleEn,
        string bodyAr,
        string bodyEn,
        Dictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[Driver Notification] Sending to Phone: {Phone}, UserId: {UserId} - Title: '{Title}', Body: '{Body}'",
            phone, driverUserId, titleAr, bodyAr);

        // 1. Persist in-app driver notification for the user (G20, G21)
        if (driverUserId.HasValue && driverUserId.Value != Guid.Empty)
        {
            try
            {
                Guid? regId = null;
                if (data.TryGetValue("registrationId", out var regIdStr) && Guid.TryParse(regIdStr, out var parsedRegId))
                {
                    regId = parsedRegId;
                }

                var type = data.GetValueOrDefault("eventKey", "driver.registration.status");
                var window = _clock.UtcNow.AddMinutes(-2);
                var alreadyExists = await _db.DriverNotifications.AnyAsync(
                    n => n.UserId == driverUserId.Value &&
                         n.SubjectId == regId &&
                         n.Type == type &&
                         n.SentAtUtc >= window, cancellationToken);

                if (!alreadyExists)
                {
                    _db.DriverNotifications.Add(new DriverNotification
                    {
                        UserId = driverUserId.Value,
                        DriverProfileId = null,
                        Type = type,
                        TitleAr = titleAr,
                        TitleEn = titleEn,
                        BodyAr = bodyAr,
                        BodyEn = bodyEn,
                        Section = "registration",
                        SubjectId = regId,
                        DeepLink = regId.HasValue ? $"/driver/status?registrationId={regId}" : null,
                        IsRead = false,
                        SentAtUtc = _clock.UtcNow
                    });
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist in-app driver notification for user {UserId}.", driverUserId);
                throw;
            }
        }

        // 2. Deliver real-time push to driver device tokens
        if (!_fcm.IsConfigured || !driverUserId.HasValue || driverUserId.Value == Guid.Empty)
        {
            return;
        }

        try
        {
            var tokens = await _db.DriverDeviceTokens
                .Where(x => x.IsActive && x.UserId == driverUserId.Value)
                .Select(x => x.Token)
                .Union(_db.CustomerDeviceTokens
                    .Where(x => x.IsActive && x.UserId == driverUserId.Value)
                    .Select(x => x.Token))
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var token in tokens)
            {
                try
                {
                    await _fcm.SendAsync(token, titleAr, bodyAr, data, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send driver FCM push notification to user {UserId}.", driverUserId);
                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query driver device tokens for user {UserId}.", driverUserId);
            throw;
        }
    }
}
