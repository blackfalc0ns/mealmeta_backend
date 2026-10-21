using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record UpdateDriverSupportProgressCommand(Guid IssueId, string Stage, string? Notes = null) : ICommand<DriverSupportProgressDto>;
public sealed record DriverSupportProgressDto(Guid IssueId, string Stage, DateTime? OccurredAtUtc, bool Idempotent);

public sealed class UpdateDriverSupportProgressHandler(IAppDbContext db, ICurrentUser user, ILocalizedMessageProvider messages, IDateTimeProvider clock,
    IApplicationTransactionExecutor transactions, IApplicationLockManager locks) : ICommandHandler<UpdateDriverSupportProgressCommand, DriverSupportProgressDto>
{
    public Task<Result<DriverSupportProgressDto>> Handle(UpdateDriverSupportProgressCommand request, CancellationToken ct)
    {
        if (!user.IsAuthenticated || !user.UserId.HasValue || user.UserType is not (UserType.DeliveryManager or UserType.Admin))
            return Task.FromResult<Result<DriverSupportProgressDto>>(Error.Forbidden("DISPATCHER_SCOPE_REQUIRED", messages.GetMessage("dispatcher_support.forbidden")));
        if (request.Stage is not ("UnderReview" or "ContactingCustomer") || request.Notes?.Length > 500)
            return Task.FromResult<Result<DriverSupportProgressDto>>(Error.Validation("DRIVER_SUPPORT_PROGRESS_INVALID", messages.GetMessage("driver_support_issue.progress_invalid")));
        return transactions.ExecuteResultAsync(ct2 => UpdateAsync(request, ct2), ct);
    }
    private async Task<Result<DriverSupportProgressDto>> UpdateAsync(UpdateDriverSupportProgressCommand request, CancellationToken ct)
    {
        var c = await db.Complaints.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.IssueId, ct);
        if (c is null || c.DriverProfileId is null || !DriverSupportIssueProjection.IsSupportedCategory(c.Category)) return Error.NotFound("DRIVER_SUPPORT_ISSUE_NOT_FOUND", messages.GetMessage("driver_support_issue.not_found"));
        await locks.LockSupportComplaintAsync(c.Id, ct);
        c = await db.Complaints.FirstOrDefaultAsync(x => x.Id == request.IssueId, ct);
        if (c is null || c.DriverProfileId is null || !DriverSupportIssueProjection.IsSupportedCategory(c.Category)) return Error.NotFound("DRIVER_SUPPORT_ISSUE_NOT_FOUND", messages.GetMessage("driver_support_issue.not_found"));
        if (user.UserType == UserType.DeliveryManager && (!user.RestaurantId.HasValue || user.RestaurantId != c.RestaurantId)) return Error.Forbidden("DISPATCHER_SCOPE_FORBIDDEN", messages.GetMessage("dispatcher_support.forbidden"));
        if (c.Status is ComplaintStatus.Resolved or ComplaintStatus.Closed or ComplaintStatus.Rejected) return Error.Conflict("DRIVER_SUPPORT_ISSUE_TERMINAL", messages.GetMessage("driver_support_issue.terminal"));
        var audits = await db.AuditLogs.Where(a => a.EntityId == c.Id && a.Action == "DriverSupportProgressUpdated").OrderBy(a => a.OccurredAtUtc).ToListAsync(ct);
        var auditedStages = audits.Select(a => StageOf(a.AfterJson)).ToHashSet(StringComparer.Ordinal);
        var current = auditedStages.Contains("ContactingCustomer") ? "ContactingCustomer"
            : auditedStages.Contains("UnderReview") || c.Status == ComplaintStatus.UnderReview ? "UnderReview" : "Submitted";
        if (current == request.Stage) return Result.Success(new DriverSupportProgressDto(c.Id, request.Stage, audits.LastOrDefault(a => StageOf(a.AfterJson) == request.Stage)?.OccurredAtUtc, true));
        if (current == "ContactingCustomer" || request.Stage == "ContactingCustomer" && current != "UnderReview") return Error.Conflict("DRIVER_SUPPORT_PROGRESS_ORDER_INVALID", messages.GetMessage("driver_support_issue.progress_order_invalid"));
        if (!c.RestaurantId.HasValue) return Error.Conflict("DRIVER_SUPPORT_ISSUE_INCOMPLETE", messages.GetMessage("driver_support_issue.incomplete"));
        var now = clock.UtcNow; c.Status = ComplaintStatus.UnderReview;
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = user.UserId,
            Action = "DriverSupportProgressUpdated",
            EntityType = nameof(Complaint),
            EntityId = c.Id,
            AfterJson = JsonSerializer.Serialize(new { Stage = request.Stage, OccurredAtUtc = now, Notes = request.Notes }),
            Reason = request.Notes,
            SourceChannel = "DispatcherDashboard",
            OccurredAtUtc = now
        });
        var driver = await db.DriverProfiles.FirstOrDefaultAsync(x => x.Id == c.DriverProfileId, ct);
        if (driver is not null) db.DriverNotifications.Add(new DriverNotification { UserId = driver.UserId, DriverProfileId = driver.Id, Type = "driver-support-progress", TitleAr = messages.GetArabicMessage("driver_support_issue.notification_title"), TitleEn = messages.GetEnglishMessage("driver_support_issue.notification_title"), BodyAr = messages.GetArabicMessage($"driver_support_issue.notification_{request.Stage}"), BodyEn = messages.GetEnglishMessage($"driver_support_issue.notification_{request.Stage}"), Section = "support", SubjectId = c.Id, DeepLink = $"/driver/support/issues/{c.Id:D}", SentAtUtc = now });
        db.OutboxMessages.Add(new MealMate.Domain.Common.OutboxMessage { EventType = "driver-support-issue-changed.v1", AggregateType = nameof(Complaint), AggregateId = c.Id.ToString("D"), DeduplicationKey = $"driver-support-issue:{c.Id:N}:progress:{request.Stage}", PayloadJson = JsonSerializer.Serialize(new { IssueId = c.Id, RestaurantId = c.RestaurantId.Value, DriverProfileId = c.DriverProfileId, Category = c.Category, Event = "progress-updated" }), CreatedAtUtc = now });
        await db.SaveChangesAsync(ct); return Result.Success(new DriverSupportProgressDto(c.Id, request.Stage, now, false));
    }
    private static string? StageOf(string? json) => DriverSupportIssueProjection.StageFromAuditJson(json);
}
