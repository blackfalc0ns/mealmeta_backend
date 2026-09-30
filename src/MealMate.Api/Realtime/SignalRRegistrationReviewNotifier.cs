using MealMate.Application.Common.Abstractions;
using Microsoft.AspNetCore.SignalR;
using MealMate.Domain.IdentityAccess;
using MealMate.Api.Resources;
using Microsoft.Extensions.Localization;

namespace MealMate.Api.Realtime;

/// <summary>
/// SignalR-backed implementation of <see cref="IRegistrationReviewNotifier"/>.
/// Sends the <c>registration-review-updated</c> event to the <c>user:{userId}</c>
/// group. The payload contains only <c>registrationId</c> and <c>changeType</c> —
/// no sensitive fields are transmitted over the wire.
/// </summary>
public sealed class SignalRRegistrationReviewNotifier : IRegistrationReviewNotifier
{
    internal const string EventName = "registration-review-updated";

    private readonly IHubContext<RestaurantReviewHub> _hubContext;
    private readonly IAppDbContext _db;
    private readonly IStringLocalizer<Messages> _messages;

    public SignalRRegistrationReviewNotifier(
        IHubContext<RestaurantReviewHub> hubContext,
        IAppDbContext db,
        IStringLocalizer<Messages> messages)
    {
        _hubContext = hubContext;
        _db = db;
        _messages = messages;
    }

    public async Task NotifyRegistrationUpdated(
        Guid userId,
        Guid registrationId,
        string changeType,
        CancellationToken cancellationToken = default)
    {
        var messageKey = changeType switch
        {
            "field-rejected" or "field.rejected" => "notifications.registration.field_rejected",
            "document-rejected" or "document.rejected" => "notifications.registration.document_rejected",
            "approved" or "registration.approved" => "notifications.registration.approved",
            _ => "notifications.registration.updated",
        };
        var titleAr = _messages[$"{messageKey}.title_ar"].Value;
        var titleEn = _messages[$"{messageKey}.title_en"].Value;
        var normalizedType = changeType switch
        {
            "approved" => "registration.approved",
            "rejected" => "registration.rejected",
            "field-rejected" => "field.rejected",
            "document-rejected" => "document.rejected",
            _ => changeType
        };

        var deepLink = normalizedType switch
        {
            "registration.approved" => "/restaurant/orders/pending-confirmation",
            "registration.rejected" => "/restaurant/under-review",
            "registration.changes_requested" or "changes-requested" => "/restaurant/onboarding?step=review&correction=true",
            "document.rejected" or "document-rejected" => "/restaurant/onboarding?step=documents&correction=true",
            "field.rejected" or "field-rejected" => "/restaurant/onboarding?correction=true",
            "meal.approved" or "meal.rejected" => $"/restaurant/operations/menu/{registrationId}",
            _ => "/restaurant/under-review"
        };

        var isHandledByPushService = normalizedType is "registration.approved"
            or "registration.rejected"
            or "registration.changes_requested"
            or "field.rejected"
            or "document.rejected"
            or "meal.approved"
            or "meal.rejected";

        if (!isHandledByPushService)
        {
            _db.RestaurantNotifications.Add(new RestaurantNotification
            {
                UserId = userId,
                Type = normalizedType,
                TitleAr = string.IsNullOrWhiteSpace(titleAr) ? "تحديث في مراجعة المطعم" : titleAr,
                TitleEn = string.IsNullOrWhiteSpace(titleEn) ? "Restaurant Review Updated" : titleEn,
                BodyAr = _messages[$"{messageKey}.body_ar"].Value,
                BodyEn = _messages[$"{messageKey}.body_en"].Value,
                DeepLink = deepLink,
                SentAtUtc = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        var group = RestaurantReviewHub.GroupForUser(userId.ToString());
        await _hubContext.Clients.Group(group).SendAsync(
            EventName,
            new { registrationId, changeType = normalizedType, deepLink },
            cancellationToken);

        await _hubContext.Clients.Group(group).SendAsync(
            "notification-received",
            new { type = normalizedType, subjectId = registrationId, deepLink },
            cancellationToken);
    }
}
