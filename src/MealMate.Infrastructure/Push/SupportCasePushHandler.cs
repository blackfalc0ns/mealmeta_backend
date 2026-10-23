using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MealMate.Application.Features.UnifiedSupport;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Push;

/// <summary>Delivers requester support pushes from the durable outbox after rechecking current ownership.</summary>
public sealed class SupportCasePushHandler(
    IAppDbContext db, IFcmMessagingClient fcm, ILogger<SupportCasePushHandler> logger)
    : INotificationHandler<OutboxLifecycleNotification>
{
    private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task Handle(OutboxLifecycleNotification message, CancellationToken cancellationToken)
    {
        if (message.EventType != SupportCasePushOutbox.EventType) return;

        var payload = JsonSerializer.Deserialize<SupportCasePushOutbox.Payload>(message.PayloadJson, PayloadOptions)
            ?? throw new InvalidOperationException("Support push payload is invalid.");
        if (payload.NotificationId == Guid.Empty || payload.SupportEventId == Guid.Empty
            || payload.RecipientUserId == Guid.Empty || payload.Audience is not ("customer" or "driver"))
            throw new InvalidOperationException("Support push payload is incomplete.");

        var inbox = payload.Audience == "customer"
            ? await db.CustomerNotifications.AsNoTracking().Where(x => x.Id == payload.NotificationId
                    && x.UserId == payload.RecipientUserId && x.SubjectId == payload.SupportEventId)
                .Select(x => new InboxItem(x.Id, x.UserId, x.EventKey, null, x.SubjectId, x.DeepLink,
                    x.TitleAr, x.TitleEn, x.BodyAr, x.BodyEn)).SingleOrDefaultAsync(cancellationToken)
            : await db.DriverNotifications.AsNoTracking().Where(x => x.Id == payload.NotificationId
                    && x.UserId == payload.RecipientUserId && x.SubjectId == payload.SupportEventId
                    && x.Section == "support")
                .Select(x => new InboxItem(x.Id, x.UserId, x.Type, x.DriverProfileId, x.SubjectId, x.DeepLink,
                    x.TitleAr, x.TitleEn, x.BodyAr, x.BodyEn)).SingleOrDefaultAsync(cancellationToken);
        if (inbox is null) return;

        var caseId = await db.SupportCaseEvents.AsNoTracking()
            .Where(x => x.Id == payload.SupportEventId)
            .Select(x => (Guid?)x.SupportCaseId).SingleOrDefaultAsync(cancellationToken);
        if (caseId is null) return;

        var supportCase = await db.SupportCases.AsNoTracking()
            .Where(x => x.Id == caseId.Value && x.RequesterUserId == payload.RecipientUserId)
            .Select(x => new { x.Channel, x.RequesterUserId, x.Id }).SingleOrDefaultAsync(cancellationToken);
        if (supportCase is null || (payload.Audience == "customer"
                ? supportCase.Channel != SupportCaseChannel.Customer
                : supportCase.Channel != SupportCaseChannel.Driver)) return;
        if (!(await db.GetActiveAccountUserIdsAsync([payload.RecipientUserId], cancellationToken))
            .Contains(payload.RecipientUserId)) return;

        string? language;
        var deviceTargets = new List<DeviceTarget>();
        if (payload.Audience == "customer")
        {
            language = await db.CustomerProfiles.AsNoTracking().Where(x => x.UserId == payload.RecipientUserId && !x.IsDeleted)
                .Select(x => x.PreferredLanguage).SingleOrDefaultAsync(cancellationToken);
            var devices = await db.CustomerDeviceTokens.Where(x => x.UserId == payload.RecipientUserId
                    && x.IsActive && !x.IsDeleted && !string.IsNullOrWhiteSpace(x.Token))
                .ToListAsync(cancellationToken);
            deviceTargets.AddRange(devices.Select(x => new DeviceTarget(x.Token, () => x.IsActive = false)));
        }
        else
        {
            var driverProfile = await db.DriverProfiles.AsNoTracking().Where(x => x.Id == inbox.DriverProfileId
                    && x.UserId == payload.RecipientUserId && !x.IsDeleted)
                .Select(x => new { x.Id }).SingleOrDefaultAsync(cancellationToken);
            if (driverProfile is null) return;
            language = await db.DriverAppSettings.AsNoTracking().Where(x => x.DriverProfileId == driverProfile.Id && !x.IsDeleted)
                .Select(x => x.PreferredLanguage).SingleOrDefaultAsync(cancellationToken);
            var devices = await db.DriverDeviceTokens.Where(x => x.UserId == payload.RecipientUserId
                    && (x.DriverProfileId == null || x.DriverProfileId == driverProfile.Id)
                    && x.IsActive && !x.IsDeleted && !string.IsNullOrWhiteSpace(x.Token))
                .ToListAsync(cancellationToken);
            deviceTargets.AddRange(devices.Select(x => new DeviceTarget(x.Token, () => x.IsActive = false)));
        }

        var distinctDevices = deviceTargets.Where(x => !string.IsNullOrWhiteSpace(x.Token))
            .GroupBy(x => x.Token, StringComparer.Ordinal).Select(x => x.ToList()).ToList();
        if (distinctDevices.Count == 0) return;
        if (!fcm.IsConfigured)
            throw new InvalidOperationException("FCM is not configured for support pushes.");

        var english = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
        var data = new Dictionary<string, string>
        {
            ["eventId"] = message.MessageId.ToString("D"),
            ["notificationId"] = inbox.Id.ToString("D"),
            ["audience"] = payload.Audience,
            ["eventKey"] = inbox.EventKey,
            ["type"] = inbox.EventKey,
            ["section"] = "support",
            ["caseId"] = supportCase.Id.ToString("D"),
            ["subjectId"] = inbox.SubjectId?.ToString("D") ?? string.Empty,
            ["deepLink"] = inbox.DeepLink ?? $"/support/cases/{supportCase.Id:D}",
            ["action"] = inbox.DeepLink ?? $"/support/cases/{supportCase.Id:D}",
            ["titleAr"] = inbox.TitleAr,
            ["titleEn"] = inbox.TitleEn,
            ["bodyAr"] = inbox.BodyAr,
            ["bodyEn"] = inbox.BodyEn,
        };

        var failed = false;
        foreach (var group in distinctDevices)
        {
            var result = await fcm.SendAsync(group[0].Token, english ? inbox.TitleEn : inbox.TitleAr,
                english ? inbox.BodyEn : inbox.BodyAr, data, cancellationToken);
            if (result.IsInvalidToken)
            {
                foreach (var target in group) target.Deactivate();
                await db.SaveChangesAsync(cancellationToken);
                logger.LogWarning("Deactivated invalid device token for support notification {NotificationId}.", inbox.Id);
            }
            else if (!result.IsSuccess)
            {
                failed = true;
                logger.LogWarning("FCM rejected support notification {NotificationId}; outbox will retry.", inbox.Id);
            }
        }

        if (failed) throw new InvalidOperationException("FCM delivery of a support notification failed.");
    }

    private sealed record InboxItem(Guid Id, Guid UserId, string EventKey, Guid? DriverProfileId, Guid? SubjectId,
        string? DeepLink, string TitleAr, string TitleEn, string BodyAr, string BodyEn);

    private sealed record DeviceTarget(string Token, Action Deactivate);
}
