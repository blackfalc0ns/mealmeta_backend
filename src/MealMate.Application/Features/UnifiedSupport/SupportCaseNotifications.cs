using MealMate.Application.Common.Abstractions;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.Enums;
using MealMate.Application.Features.DriverDelivery.SupervisorNotifications;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.UnifiedSupport;

public sealed partial class SupportCaseService
{
    private async Task<int> NotifyPlatformOperatorsAsync(SupportCase supportCase, SupportCaseEvent supportEvent,
        DateTime now, string eventKey, CancellationToken ct, Guid? additionalRecipientId = null)
    {
        var eligible = await _access.GetEligibleOperatorsAsync(supportCase, requireManage: false, ct);
        var eligibleIds = eligible.Select(x => x.UserId).ToHashSet();
        var recipients = supportCase.AssignedToUserId is { } assigned && eligibleIds.Contains(assigned)
            ? new HashSet<Guid> { assigned }
            : eligibleIds;
        if (additionalRecipientId is { } additional && eligibleIds.Contains(additional)) recipients.Add(additional);
        recipients.Remove(supportEvent.ActorUserId);
        foreach (var recipient in recipients)
        {
            _db.AdminNotifications.Add(new AdminNotification
            {
                UserId = recipient,
                TitleAr = eventKey == "support.case.created" ? SupportCaseNotificationCopy.PlatformCaseTitleAr : SupportCaseNotificationCopy.PlatformCaseUpdateTitleAr,
                TitleEn = eventKey == "support.case.created" ? SupportCaseNotificationCopy.PlatformCaseTitleEn : SupportCaseNotificationCopy.PlatformCaseUpdateTitleEn,
                BodyAr = eventKey == "support.case.created" ? SupportCaseNotificationCopy.PlatformCaseBodyAr : SupportCaseNotificationCopy.PlatformCaseUpdateBodyAr,
                BodyEn = eventKey == "support.case.created" ? SupportCaseNotificationCopy.PlatformCaseBodyEn : SupportCaseNotificationCopy.PlatformCaseUpdateBodyEn,
                Channel = MealMate.Domain.Enums.NotificationChannel.InApp,
                IsRead = false,
                SentAtUtc = now,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    deepLink = $"/admin/support/complaints?caseId={supportCase.Id:D}",
                    eventKey,
                    supportEventId = supportEvent.Id,
                }),
                CreatedAtUtc = now,
                CreatedByUserId = supportEvent.ActorUserId,
            });
        }
        return recipients.Count;
    }

    private async Task NotifyRequesterAsync(SupportCase supportCase, SupportCaseEvent supportEvent, DateTime now, CancellationToken ct)
    {
        var recipient = supportCase.RequesterUserId;
        if (recipient == ActorId || !(await _db.GetActiveAccountUserIdsAsync([recipient], ct)).Contains(recipient)) return;
        var titleAr = SupportCaseNotificationCopy.CaseUpdatedTitleAr;
        var titleEn = SupportCaseNotificationCopy.CaseUpdatedTitleEn;
        var bodyAr = SupportCaseNotificationCopy.CaseUpdatedBodyAr;
        var bodyEn = SupportCaseNotificationCopy.CaseUpdatedBodyEn;
        var path = $"/support/cases/{supportCase.Id:D}";
        switch (supportCase.Channel)
        {
            case SupportCaseChannel.Customer:
                SupportCasePushOutbox.Add(_db, new CustomerNotification
                {
                    UserId = recipient, EventKey = "support.case.updated", SubjectId = supportEvent.Id,
                    TitleAr = titleAr, TitleEn = titleEn, BodyAr = bodyAr, BodyEn = bodyEn,
                    DeepLink = path, CreatedAtUtc = now, CreatedByUserId = ActorId,
                }, supportEvent.Id);
                break;
            case SupportCaseChannel.Driver:
                var driver = await _db.DriverProfiles.AsNoTracking().Where(x => x.UserId == recipient)
                    .Select(x => new { x.Id }).SingleOrDefaultAsync(ct);
                if (driver is null) return;
                SupportCasePushOutbox.Add(_db, new DriverNotification
                {
                    UserId = recipient, DriverProfileId = driver.Id, Type = "support-case-updated",
                    TitleAr = titleAr, TitleEn = titleEn, BodyAr = bodyAr, BodyEn = bodyEn,
                    Section = "support", SubjectId = supportEvent.Id, DeepLink = path, SentAtUtc = now,
                    CreatedAtUtc = now, CreatedByUserId = ActorId,
                }, supportEvent.Id);
                break;
            case SupportCaseChannel.Restaurant:
                _db.RestaurantNotifications.Add(new RestaurantNotification
                {
                    UserId = recipient, Type = "support-case-updated", TitleAr = titleAr, TitleEn = titleEn,
                    BodyAr = bodyAr, BodyEn = bodyEn, Section = "support", SubjectId = supportEvent.Id,
                    DeepLink = $"/restaurant/support?caseId={supportCase.Id:D}", SentAtUtc = now, CreatedAtUtc = now, CreatedByUserId = ActorId,
                });
                break;
            case SupportCaseChannel.DeliveryManager:
                DeliveryManagerSupportPushOutbox.Add(_db, new DeliveryManagerNotification
                {
                    UserId = recipient, RestaurantId = supportCase.RestaurantId, Type = "support-case-updated",
                    TitleAr = titleAr, TitleEn = titleEn, BodyAr = bodyAr, BodyEn = bodyEn,
                    Section = "support", SubjectId = supportEvent.Id, DeepLink = path, SentAtUtc = now,
                    CreatedAtUtc = now, CreatedByUserId = ActorId,
                });
                break;
        }
    }

    private async Task NotifyTaskRecipientAsync(SupportCase supportCase, SupportCaseTask task, SupportCaseEvent supportEvent,
        DateTime now, CancellationToken ct, bool assignment = true)
    {
        var titleAr = assignment ? SupportCaseNotificationCopy.TaskAssignedTitleAr : SupportCaseNotificationCopy.TaskUpdatedTitleAr;
        var titleEn = assignment ? SupportCaseNotificationCopy.TaskAssignedTitleEn : SupportCaseNotificationCopy.TaskUpdatedTitleEn;
        var bodyAr = assignment ? SupportCaseNotificationCopy.TaskAssignedBodyAr : SupportCaseNotificationCopy.TaskUpdatedBodyAr;
        var bodyEn = assignment ? SupportCaseNotificationCopy.TaskAssignedBodyEn : SupportCaseNotificationCopy.TaskUpdatedBodyEn;
        Guid recipient;
        if (task.RecipientType == SupportCaseTaskRecipientType.DeliveryManager)
        {
            if (task.RecipientUserId is not { } dmId || task.RestaurantId is not { } restaurantId
                || !(await _db.GetDeliveryManagerUserIdsAsync(restaurantId, ct)).Contains(dmId)) return;
            recipient = dmId;
            if (recipient == ActorId) return;
            DeliveryManagerSupportPushOutbox.Add(_db, new DeliveryManagerNotification
            {
                UserId = recipient, RestaurantId = restaurantId, Type = "support-task-assigned",
                TitleAr = titleAr, TitleEn = titleEn,
                BodyAr = bodyAr, BodyEn = bodyEn,
                    Section = "support", SubjectId = supportEvent.Id, DeepLink = $"/support/cases/{supportCase.Id:D}",
                SentAtUtc = now, CreatedAtUtc = now, CreatedByUserId = ActorId,
            });
            return;
        }

        if (task.RestaurantId is not { } taskRestaurantId) return;
        var restaurantUserId = await _db.RestaurantProfiles.AsNoTracking()
            .Where(x => x.Id == taskRestaurantId && !x.IsDeleted)
            .Select(x => x.UserId).SingleOrDefaultAsync(ct);
        if (restaurantUserId == Guid.Empty || restaurantUserId == ActorId
            || !(await _db.GetActiveAccountUserIdsAsync([restaurantUserId], ct)).Contains(restaurantUserId)) return;
        var restaurantActor = restaurantUserId;
        _db.RestaurantNotifications.Add(new RestaurantNotification
        {
            UserId = restaurantActor, Type = "support-task-assigned",
            TitleAr = titleAr, TitleEn = titleEn,
            BodyAr = bodyAr, BodyEn = bodyEn,
            Section = "support", SubjectId = supportEvent.Id, DeepLink = $"/restaurant/support?caseId={supportCase.Id:D}",
            SentAtUtc = now, CreatedAtUtc = now, CreatedByUserId = ActorId,
        });
    }
}
