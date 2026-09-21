using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MealMate.Application.Common.Abstractions;
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

    public DriverRegistrationNotificationService(
        IAppDbContext db,
        IFcmMessagingClient fcm,
        IDateTimeProvider clock,
        ILogger<DriverRegistrationNotificationService> logger)
    {
        _db = db;
        _fcm = fcm;
        _clock = clock;
        _logger = logger;
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

        var titleAr = "طلب تسجيل سائق جديد 🚗";
        var titleEn = "New Driver Registration Application 🚗";
        var bodyAr = $"قدم السائق \"{driverName}\" ({phone}) طلب انضمام لمطعمك، يرجى المراجعة والتدقيق.";
        var bodyEn = $"Driver \"{driverName}\" ({phone}) submitted an application to join your restaurant. Please review.";
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist restaurant notification for driver registration {RegistrationId}.", registrationId);
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

        var titleAr = "إعادة تقديم طلب سائق 🔄";
        var titleEn = "Driver Application Resubmitted 🔄";
        var bodyAr = $"قام السائق \"{driverName}\" بتحديث البيانات والمستندات المطلوبة، وهي بانتظار مراجعتك.";
        var bodyEn = $"Driver \"{driverName}\" updated their application details/documents and is ready for your review.";
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist restaurant resubmission notification for driver registration {RegistrationId}.", registrationId);
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
        var titleAr = "طلب سائق معتمد من المطعم بانتظار التأكيد 📋";
        var titleEn = "Driver Application Ready for Final Confirmation 📋";
        var bodyAr = $"وافق مطعم \"{restaurantName}\" على طلب السائق \"{driverName}\" ({phone})، وبانتظار اعتماد وتأكيد الإدارة النهائي.";
        var bodyEn = $"Restaurant \"{restaurantName}\" approved driver \"{driverName}\" ({phone}). Pending final admin verification and confirmation.";
        var deepLink = $"/admin/drivers/registrations/{registrationId}";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "driver.registration.restaurant_approved",
            ["registrationId"] = registrationId.ToString(),
            ["restaurantId"] = restaurantId.ToString(),
            ["restaurantName"] = restaurantName,
            ["driverName"] = driverName,
            ["phone"] = phone,
            ["deepLink"] = deepLink,
        };

        // 1. Persist in-app notification for admins
        try
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist admin notification for approved driver registration {RegistrationId}.", registrationId);
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
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query admin device tokens for driver registration {RegistrationId}.", registrationId);
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
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query restaurant device tokens for restaurant {RestaurantId}.", restaurantId);
        }
    }
}
