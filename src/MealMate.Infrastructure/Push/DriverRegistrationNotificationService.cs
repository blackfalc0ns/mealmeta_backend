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

    public async Task NotifyDriverApplicationSubmittedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string restaurantName,
        CancellationToken cancellationToken = default)
    {
        var titleAr = "تم استلام طلب تسجيلك 📋";
        var titleEn = "Driver Application Submitted 📋";
        var bodyAr = $"تم استلام طلب انضمامك بنجاح وهو قيد المراجعة حالياً لدى مطعم \"{restaurantName}\". سنقوم بإشعارك فور اتخاذ القرار.";
        var bodyEn = $"Your application has been received and is currently under review by \"{restaurantName}\". We will notify you once a decision is made.";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "driver.registration.submitted",
            ["registrationId"] = registrationId.ToString(),
            ["stage"] = "1",
            ["status"] = "Submitted",
            ["restaurantName"] = restaurantName,
        };

        await SendDriverPushAsync(driverUserId, phone, titleAr, bodyAr, data, cancellationToken);
    }

    public async Task NotifyDriverRestaurantApprovedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string restaurantName,
        CancellationToken cancellationToken = default)
    {
        var titleAr = "موافقة المطعم على طلبك 🎉";
        var titleEn = "Restaurant Approved Your Application 🎉";
        var bodyAr = $"وافق مطعم \"{restaurantName}\" على طلب انضمامك! طلبك الآن قيد الاعتماد النهائي من إدارة المنصة وتفعيل الحساب.";
        var bodyEn = $"\"{restaurantName}\" approved your application! It is now pending final activation by platform administration.";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "driver.registration.restaurant_approved",
            ["registrationId"] = registrationId.ToString(),
            ["stage"] = "1",
            ["status"] = "UnderReview",
            ["restaurantName"] = restaurantName,
        };

        await SendDriverPushAsync(driverUserId, phone, titleAr, bodyAr, data, cancellationToken);
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
        var titleAr = "مطلوب استكمال نواقص في طلبك ⚠️";
        var titleEn = "Action Required: Missing Information ⚠️";
        var bodyAr = $"طلب مطعم \"{restaurantName}\" استكمال نواقص أو تعديل بيانات: \"{notes}\". يرجى فتح التطبيق للتعديل وإعادة التقديم.";
        var bodyEn = $"\"{restaurantName}\" requested changes to your application: \"{notes}\". Please open the app to update and resubmit.";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "driver.registration.restaurant_changes_requested",
            ["registrationId"] = registrationId.ToString(),
            ["stage"] = "2",
            ["status"] = "NeedsChanges",
            ["notes"] = notes,
            ["restaurantName"] = restaurantName,
        };

        await SendDriverPushAsync(driverUserId, phone, titleAr, bodyAr, data, cancellationToken);
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
        var titleAr = "تحديث بخصوص طلب الانضمام";
        var titleEn = "Update on Your Driver Application";
        var bodyAr = $"نعتذر، تم رفض طلب الانضمام إلى مطعم \"{restaurantName}\". سبب الرفض: \"{reason}\".";
        var bodyEn = $"Unfortunately, your application to join \"{restaurantName}\" was rejected. Reason: \"{reason}\".";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "driver.registration.restaurant_rejected",
            ["registrationId"] = registrationId.ToString(),
            ["stage"] = "3",
            ["status"] = "Rejected",
            ["reason"] = reason,
            ["restaurantName"] = restaurantName,
        };

        await SendDriverPushAsync(driverUserId, phone, titleAr, bodyAr, data, cancellationToken);
    }

    public async Task NotifyDriverAdminApprovedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string restaurantName,
        CancellationToken cancellationToken = default)
    {
        var titleAr = "تم اعتماد حسابك بنجاح! 🚀";
        var titleEn = "Your Account is Approved! 🚀";
        var bodyAr = $"تهانينا! تم اعتماد وتفعيل حسابك كسائق رسمي في MealMate لمطعم \"{restaurantName}\". يمكنك الآن تسجيل الدخول والبدء في استلام الرحلات.";
        var bodyEn = $"Congratulations! Your driver account has been approved and activated for \"{restaurantName}\". You can now login and start deliveries.";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "driver.registration.admin_approved",
            ["registrationId"] = registrationId.ToString(),
            ["stage"] = "4",
            ["status"] = "Approved",
            ["restaurantName"] = restaurantName,
        };

        await SendDriverPushAsync(driverUserId, phone, titleAr, bodyAr, data, cancellationToken);
    }

    public async Task NotifyDriverAdminChangesRequestedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string notes,
        CancellationToken cancellationToken = default)
    {
        var titleAr = "مطلوب تعديلات من إدارة المنصة ⚠️";
        var titleEn = "Action Required from Platform Administration ⚠️";
        var bodyAr = $"طلبت إدارة المنصة تعديل بيانات أو وثائق: \"{notes}\". يرجى فتح التطبيق والتحديث.";
        var bodyEn = $"Platform administration requested updates to your registration: \"{notes}\". Please open the app to update.";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "driver.registration.admin_changes_requested",
            ["registrationId"] = registrationId.ToString(),
            ["stage"] = "2",
            ["status"] = "NeedsChanges",
            ["notes"] = notes,
        };

        await SendDriverPushAsync(driverUserId, phone, titleAr, bodyAr, data, cancellationToken);
    }

    public async Task NotifyDriverAdminRejectedAsync(
        Guid registrationId,
        Guid? driverUserId,
        string phone,
        string driverName,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var titleAr = "تم رفض طلب التسجيل من الإدارة";
        var titleEn = "Application Rejected by Administration";
        var bodyAr = $"نعتذر، تم رفض طلب التسجيل من قبل إدارة المنصة. سبب الرفض: \"{reason}\".";
        var bodyEn = $"Unfortunately, your driver application was rejected by administration. Reason: \"{reason}\".";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "driver.registration.admin_rejected",
            ["registrationId"] = registrationId.ToString(),
            ["stage"] = "3",
            ["status"] = "Rejected",
            ["reason"] = reason,
        };

        await SendDriverPushAsync(driverUserId, phone, titleAr, bodyAr, data, cancellationToken);
    }

    private async Task SendDriverPushAsync(
        Guid? driverUserId,
        string phone,
        string title,
        string body,
        Dictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[Driver Notification] Sending to Phone: {Phone}, UserId: {UserId} - Title: '{Title}', Body: '{Body}'",
            phone, driverUserId, title, body);

        if (!_fcm.IsConfigured || !driverUserId.HasValue || driverUserId.Value == Guid.Empty)
        {
            return;
        }

        try
        {
            var tokens = await _db.CustomerDeviceTokens
                .Where(x => x.IsActive && x.UserId == driverUserId.Value)
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
                    _logger.LogWarning(ex, "Failed to send driver FCM push notification to user {UserId}.", driverUserId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query driver device tokens for user {UserId}.", driverUserId);
        }
    }
}

