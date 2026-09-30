using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Push;

public sealed class MealPushNotifier : IMealPushNotifier
{
    private readonly IAppDbContext _db;
    private readonly IFcmMessagingClient _fcm;
    private readonly IRegistrationReviewNotifier _realtimeNotifier;
    private readonly ILogger<MealPushNotifier>? _logger;

    public MealPushNotifier(
        IAppDbContext db,
        IFcmMessagingClient fcm,
        IRegistrationReviewNotifier? realtimeNotifier = null,
        ILogger<MealPushNotifier>? logger = null)
    {
        _db = db;
        _fcm = fcm;
        _realtimeNotifier = realtimeNotifier ?? NullRegistrationReviewNotifier.Instance;
        _logger = logger;
    }

    public async Task NotifyAdminsMealSubmittedAsync(
        Guid mealId,
        Guid restaurantId,
        string mealNameAr,
        string mealNameEn,
        CancellationToken cancellationToken = default)
    {
        var titleAr = "طلب اعتماد وجبة جديدة 🍽️";
        var titleEn = "New Meal Submitted for Review";
        var bodyAr = $"تم تقديم وجبة \"{mealNameAr}\" للمراجعة والاعتماد.";
        var bodyEn = $"Meal \"{mealNameEn}\" was submitted for review.";
        var deepLink = $"/admin/operations/menus?mealId={mealId}";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "meal.submitted",
            ["mealId"] = mealId.ToString(),
            ["restaurantId"] = restaurantId.ToString(),
            ["deepLink"] = deepLink
        };

        try
        {
            _db.AdminNotifications.Add(new AdminNotification
            {
                TitleAr = titleAr,
                TitleEn = titleEn,
                BodyAr = bodyAr,
                BodyEn = bodyEn,
                Channel = NotificationChannel.InApp,
                IsRead = false,
                SentAtUtc = DateTime.UtcNow,
                PayloadJson = JsonSerializer.Serialize(data)
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist admin in-app notification for meal {MealId}.", mealId);
        }

        if (!_fcm.IsConfigured) return;

        var tokens = await _db.AdminDeviceTokens
            .Where(x => x.IsActive)
            .Select(x => x.Token)
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
                _logger?.LogWarning(ex, "Failed to send admin push for meal {MealId}.", mealId);
            }
        }
    }

    public async Task NotifyRestaurantMealApprovedAsync(
        Guid mealId,
        Guid restaurantId,
        string mealNameAr,
        string mealNameEn,
        CancellationToken cancellationToken = default)
    {
        var titleAr = "تم اعتماد الوجبة بنجاح 🎉";
        var titleEn = "Meal Approved 🎉";
        var bodyAr = $"تمت الموافقة على وجبة \"{mealNameAr}\" من قبل الإدارة ويمكنك الآن إضافتها لجداول وجباتك.";
        var bodyEn = $"Meal \"{mealNameEn}\" has been approved and is available for scheduling.";
        var deepLink = $"/restaurant/operations/menu/{mealId}";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "meal.approved",
            ["mealId"] = mealId.ToString(),
            ["restaurantId"] = restaurantId.ToString(),
            ["deepLink"] = deepLink
        };

        var profile = await _db.RestaurantProfiles.FirstOrDefaultAsync(p => p.Id == restaurantId, cancellationToken);
        var ownerUserId = profile?.UserId;

        if (ownerUserId.HasValue && ownerUserId.Value != Guid.Empty)
        {
            try
            {
                _db.RestaurantNotifications.Add(new RestaurantNotification
                {
                    UserId = ownerUserId.Value,
                    Type = "meal.approved",
                    TitleAr = titleAr,
                    TitleEn = titleEn,
                    BodyAr = bodyAr,
                    BodyEn = bodyEn,
                    SubjectId = mealId,
                    DeepLink = deepLink,
                    IsRead = false,
                    SentAtUtc = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to persist restaurant in-app notification for approved meal {MealId}.", mealId);
            }

            try
            {
                await _realtimeNotifier.NotifyRegistrationUpdated(
                    ownerUserId.Value,
                    mealId,
                    "meal.approved",
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to send realtime SignalR notification for approved meal {MealId}.", mealId);
            }
        }

        if (!_fcm.IsConfigured) return;

        var tokens = await _db.RestaurantDeviceTokens
            .Where(x => (x.RestaurantId == restaurantId || (ownerUserId.HasValue && x.UserId == ownerUserId.Value)) && x.IsActive)
            .Select(x => x.Token)
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
                _logger?.LogWarning(ex, "Failed to send restaurant push for approved meal {MealId}.", mealId);
            }
        }
    }

    public async Task NotifyRestaurantMealRejectedAsync(
        Guid mealId,
        Guid restaurantId,
        string mealNameAr,
        string mealNameEn,
        string rejectionReason,
        CancellationToken cancellationToken = default)
    {
        var titleAr = "ملاحظات على مراجعة الوجبة ⚠️";
        var titleEn = "Meal Review Update ⚠️";
        var bodyAr = $"تم رفض وجبة \"{mealNameAr}\": {rejectionReason}.";
        var bodyEn = $"Meal \"{mealNameEn}\" was rejected: {rejectionReason}.";
        var deepLink = $"/restaurant/operations/menu/{mealId}";

        var data = new Dictionary<string, string>
        {
            ["eventKey"] = "meal.rejected",
            ["mealId"] = mealId.ToString(),
            ["restaurantId"] = restaurantId.ToString(),
            ["reason"] = rejectionReason,
            ["deepLink"] = deepLink
        };

        var profile = await _db.RestaurantProfiles.FirstOrDefaultAsync(p => p.Id == restaurantId, cancellationToken);
        var ownerUserId = profile?.UserId;

        if (ownerUserId.HasValue && ownerUserId.Value != Guid.Empty)
        {
            try
            {
                _db.RestaurantNotifications.Add(new RestaurantNotification
                {
                    UserId = ownerUserId.Value,
                    Type = "meal.rejected",
                    TitleAr = titleAr,
                    TitleEn = titleEn,
                    BodyAr = bodyAr,
                    BodyEn = bodyEn,
                    SubjectId = mealId,
                    DeepLink = deepLink,
                    IsRead = false,
                    SentAtUtc = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to persist restaurant in-app notification for rejected meal {MealId}.", mealId);
            }

            try
            {
                await _realtimeNotifier.NotifyRegistrationUpdated(
                    ownerUserId.Value,
                    mealId,
                    "meal.rejected",
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to send realtime SignalR notification for rejected meal {MealId}.", mealId);
            }
        }

        if (!_fcm.IsConfigured) return;

        var tokens = await _db.RestaurantDeviceTokens
            .Where(x => (x.RestaurantId == restaurantId || (ownerUserId.HasValue && x.UserId == ownerUserId.Value)) && x.IsActive)
            .Select(x => x.Token)
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
                _logger?.LogWarning(ex, "Failed to send restaurant push for rejected meal {MealId}.", mealId);
            }
        }
    }
}
