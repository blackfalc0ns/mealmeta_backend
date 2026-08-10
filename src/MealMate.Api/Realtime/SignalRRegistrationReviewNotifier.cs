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

    public Task NotifyRegistrationUpdated(
        Guid userId,
        Guid registrationId,
        string changeType,
        CancellationToken cancellationToken = default)
    {
        var messageKey = changeType switch
        {
            "field-rejected" => "notifications.registration.field_rejected",
            "document-rejected" => "notifications.registration.document_rejected",
            "approved" => "notifications.registration.approved",
            _ => "notifications.registration.updated",
        };
        var titleAr = _messages[$"{messageKey}.title_ar"].Value;
        var titleEn = _messages[$"{messageKey}.title_en"].Value;
        _db.RestaurantNotifications.Add(new RestaurantNotification
        {
            UserId = userId,
            Type = changeType,
            TitleAr = titleAr,
            TitleEn = titleEn,
            BodyAr = _messages[$"{messageKey}.body_ar"].Value,
            BodyEn = _messages[$"{messageKey}.body_en"].Value,
            SentAtUtc = DateTime.UtcNow,
        });
        var group = RestaurantReviewHub.GroupForUser(userId.ToString());
        return _hubContext.Clients.Group(group).SendAsync(
            EventName,
            new { registrationId, changeType },
            cancellationToken);
    }
}
