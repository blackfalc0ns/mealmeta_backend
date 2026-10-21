using MealMate.Application.Features.DriverDelivery.SupervisorNotifications;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record ResolveSupportIssueCommand(
    Guid IssueId,
    string? ResolutionNotes = null) : ICommand<ResolveSupportIssueResponseDto>;

public sealed class ResolveSupportIssueCommandHandler : ICommandHandler<ResolveSupportIssueCommand, ResolveSupportIssueResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IAuditLogger _audit;
    private readonly ICurrentUser? _currentUser;
    private readonly IApplicationTransactionExecutor? _transactions;
    private readonly IApplicationLockManager? _locks;

    public ResolveSupportIssueCommandHandler(
        IAppDbContext db,
        ILocalizedMessageProvider messages,
        IAuditLogger audit,
        ICurrentUser? currentUser = null,
        IApplicationTransactionExecutor? transactions = null,
        IApplicationLockManager? locks = null)
    {
        _db = db;
        _messages = messages;
        _audit = audit;
        _currentUser = currentUser;
        _transactions = transactions;
        _locks = locks;
    }

    public async Task<Result<ResolveSupportIssueResponseDto>> Handle(
        ResolveSupportIssueCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser is null || !_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue
            || _currentUser.UserType is not (UserType.DeliveryManager or UserType.Admin))
            return Error.Forbidden("DISPATCHER_SCOPE_REQUIRED", _messages.GetMessage("dispatcher_support.forbidden", "Dispatcher access is required."));
        if (_transactions is null || _locks is null)
            return Error.Conflict("SUPPORT_TRANSACTION_UNAVAILABLE", _messages.GetMessage("dispatcher_support.transaction_unavailable", "Support resolution requires a transaction and application locks."));
        return await _transactions.ExecuteResultAsync(token => HandleLockedAsync(request, token), cancellationToken);
    }

    private async Task<Result<ResolveSupportIssueResponseDto>> HandleLockedAsync(ResolveSupportIssueCommand request, CancellationToken cancellationToken)
    {
        var isAr = _messages.IsArabic();

        // 1. Validate resolution notes
        var trimmedNotes = request.ResolutionNotes?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedNotes) || trimmedNotes.Length < 3 || trimmedNotes.Length > 500)
        {
            return Error.Validation(
                "NOTES_INVALID",
                isAr ? "ملاحظات الحل مطلوبة ويجب أن تكون بين 3 و 500 حرف" : "Resolution notes are required and must be between 3 and 500 characters.");
        }

        var now = DateTime.UtcNow;
        Guid? restaurantId = null;
        var category = "DriverSupportIssue";

        // 2. Fetch complaint or stop
        var complaint = await _db.Complaints.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.IssueId, cancellationToken);

        Guid? driverId = null;
        if (complaint != null)
        {
            await _locks!.LockSupportComplaintAsync(complaint.Id, cancellationToken);
            complaint = await _db.Complaints.Include(c => c.AdminReviews).FirstOrDefaultAsync(c => c.Id == request.IssueId, cancellationToken);
            if (complaint is null) return Error.NotFound(MessageCodes.DispatcherSupport.IssueNotFound, _messages.GetMessage(MessageCodes.DispatcherSupport.IssueNotFound, "Support issue was not found."));
            if (_currentUser!.UserType == UserType.DeliveryManager && _currentUser.RestaurantId != complaint.RestaurantId)
                return Error.Forbidden("DISPATCHER_SCOPE_FORBIDDEN", _messages.GetMessage("dispatcher_support.forbidden", "You cannot access this restaurant issue."));
            if (complaint.Status == ComplaintStatus.Resolved || complaint.Status == ComplaintStatus.Closed)
            {
                return Error.Conflict(
                    "ISSUE_ALREADY_RESOLVED",
                    isAr ? "تم حل المشكلة بالفعل" : "Issue is already resolved.");
            }

            if (await _db.DriverSupportHandovers.AnyAsync(x => x.ComplaintId == complaint.Id && (x.Status == "Assigned" || x.Status == "Arrived" || x.Status == "Receiving"), cancellationToken))
                return Error.Conflict("HANDOVER_PENDING", _messages.GetMessage("driver_support_handover.pending", "Complete or cancel the active driver handover before resolving this issue."));
            if (complaint.Category == "DriverReassignmentRequested"
                && complaint.ReassignmentDecision is null
                && !await _db.DriverSupportHandovers.AnyAsync(x => x.ComplaintId == complaint.Id, cancellationToken))
                return Error.Conflict("REASSIGNMENT_DECISION_REQUIRED", _messages.GetMessage("driver_reassignment.decision_required"));
            if (await _db.DeliveryHolds.AnyAsync(x => x.DriverSupportHandoverBoxId.HasValue && x.Status == "Active"
                && x.ReleasedAtUtc == null && x.ResolvedAtUtc == null
                && _db.DriverSupportHandoverBoxes.Any(b => b.Id == x.DriverSupportHandoverBoxId.Value
                    && _db.DriverSupportHandovers.Any(h => h.Id == b.HandoverId
                        && (h.ComplaintId == complaint.Id || h.RequestedIssueId == request.IssueId))), cancellationToken))
                return Error.Conflict("SUPPORT_FOLLOWUP_PENDING", _messages.GetMessage("driver_support_handover.followup_active", "Resolve the handover follow-up before resolving this issue."));

            driverId = complaint.DriverProfileId;
            restaurantId = complaint.RestaurantId;
            category = complaint.Category;
            complaint.Status = ComplaintStatus.Resolved;
        }
        else
        {
            var stop = await _db.TripStops.AsNoTracking().Include(s => s.Trip).FirstOrDefaultAsync(s => s.Id == request.IssueId, cancellationToken);

            if (stop is null)
            {
                return Error.NotFound(
                    MessageCodes.DispatcherSupport.IssueNotFound,
                    _messages.GetMessage(MessageCodes.DispatcherSupport.IssueNotFound, "Support issue was not found."));
            }

            if (_currentUser!.UserType == UserType.DeliveryManager && _currentUser.RestaurantId != stop.Trip.RestaurantId)
                return Error.Forbidden("DISPATCHER_SCOPE_FORBIDDEN", _messages.GetMessage("dispatcher_support.forbidden", "You cannot access this restaurant issue."));
            await _locks!.LockTripAsync(stop.TripId, cancellationToken);
            stop = await _db.TripStops.FirstOrDefaultAsync(s => s.Id == request.IssueId, cancellationToken);
            if (stop is null) return Error.NotFound(MessageCodes.DispatcherSupport.IssueNotFound, _messages.GetMessage(MessageCodes.DispatcherSupport.IssueNotFound, "Support issue was not found."));
            if (await _db.DriverSupportHandovers.AnyAsync(x => x.TripId == stop.TripId && (x.Status == "Assigned" || x.Status == "Arrived" || x.Status == "Receiving"), cancellationToken))
                return Error.Conflict("HANDOVER_PENDING", _messages.GetMessage("driver_support_handover.pending", "Complete or cancel the active driver handover before resolving this issue."));
            if (await _db.DeliveryHolds.AnyAsync(x => x.DriverSupportHandoverBoxId.HasValue
                && x.Status == "Active" && x.ReleasedAtUtc == null && x.ResolvedAtUtc == null
                && _db.DriverSupportHandoverBoxes.Any(b => b.Id == x.DriverSupportHandoverBoxId.Value
                    && _db.DriverSupportHandovers.Any(h => h.Id == b.HandoverId && h.RequestedIssueId == request.IssueId)), cancellationToken))
                return Error.Conflict("SUPPORT_FOLLOWUP_PENDING", _messages.GetMessage("driver_support_handover.followup_active", "Resolve the handover follow-up before resolving this issue."));

            if (await _db.AuditLogs.AnyAsync(a => a.EntityId == request.IssueId && a.Action == "SupportIssueResolved", cancellationToken))
                return Error.Conflict("ISSUE_ALREADY_RESOLVED", _messages.GetMessage("dispatcher_support.already_resolved", "The support issue is already resolved."));

            if (stop.FailureReason == null && stop.Status != TripStopStatus.Failed)
            {
                return Error.Conflict(
                    "ISSUE_ALREADY_RESOLVED",
                    isAr ? "تم حل المشكلة بالفعل" : "Issue is already resolved.");
            }

            var driverAssignment = await _db.DriverAssignments.AsNoTracking()
                .Where(da => da.TripId == stop.TripId && da.Status == "Active")
                .OrderByDescending(da => da.AssignedAtUtc)
                .Select(da => new { da.DriverProfileId })
                .FirstOrDefaultAsync(cancellationToken);
            driverId = driverAssignment?.DriverProfileId;
            restaurantId = stop.Trip.RestaurantId;

            // Preserve the original failure evidence on the stop. Resolution state is
            // recorded in the audit log and projected from that persisted record.
        }

        // 3. Audit log
        await _audit.LogAsync(new AuditEntry(
            Action: "SupportIssueResolved",
            EntityType: nameof(Complaint),
            EntityId: request.IssueId,
            Before: null,
            After: new
            {
                request.IssueId,
                ResolutionNotes = trimmedNotes,
                ResolvedAtUtc = now,
                ResolvedById = _currentUser!.UserId
            },
            Reason: trimmedNotes,
            SourceChannel: "DispatcherDashboard"), cancellationToken);

        if (restaurantId.HasValue)
        {
            var managerUserIds = _db.DeliveryManagerRegistrations
                .Where(x => x.RestaurantId == restaurantId.Value && x.Status == VerificationStatus.Approved && x.ResultingUserId.HasValue)
                .Select(x => x.ResultingUserId!.Value).Distinct().ToArray();
            foreach (var managerUserId in managerUserIds)
                DeliveryManagerSupportPushOutbox.Add(_db, new MealMate.Domain.IdentityAccess.DeliveryManagerNotification
                {
                    UserId = managerUserId, RestaurantId = restaurantId, Type = "driver-support-issue-resolved",
                    TitleAr = _messages.GetArabicMessage("driver_support_issue.notification_title", "تحديث بلاغ دعم السائق"),
                    TitleEn = _messages.GetEnglishMessage("driver_support_issue.notification_title_en", "Driver support issue update"),
                    BodyAr = _messages.GetArabicMessage("driver_support_issue.notification_resolved", "تم حل بلاغ دعم السائق."),
                    BodyEn = _messages.GetEnglishMessage("driver_support_issue.notification_resolved_en", "A driver support issue was resolved."),
                    Section = "support", SubjectId = request.IssueId, DeepLink = $"/dispatcher/support/issues/{request.IssueId:D}", SentAtUtc = now
                });
            if (driverId.HasValue)
            {
                var driverUserId = await _db.DriverProfiles.Where(x => x.Id == driverId.Value).Select(x => (Guid?)x.UserId).FirstOrDefaultAsync(cancellationToken);
                if (driverUserId.HasValue)
                    _db.DriverNotifications.Add(new MealMate.Domain.IdentityAccess.DriverNotification
                    {
                        UserId = driverUserId.Value, DriverProfileId = driverId.Value, Type = "driver-support-issue-resolved",
                        TitleAr = _messages.GetArabicMessage("driver_support_issue.notification_title", "تحديث بلاغ دعم السائق"),
                        TitleEn = _messages.GetEnglishMessage("driver_support_issue.notification_title_en", "Driver support issue update"),
                        BodyAr = _messages.GetArabicMessage("driver_support_issue.notification_resolved", "تم حل بلاغ دعم السائق."),
                        BodyEn = _messages.GetEnglishMessage("driver_support_issue.notification_resolved_en", "A driver support issue was resolved."),
                        Section = "support", SubjectId = request.IssueId, DeepLink = $"/driver/support/issues/{request.IssueId:D}", SentAtUtc = now
                    });
            }
            _db.OutboxMessages.Add(new MealMate.Domain.Common.OutboxMessage
            {
                EventType = "driver-support-issue-changed.v1", AggregateType = nameof(Complaint), AggregateId = request.IssueId.ToString("D"),
                DeduplicationKey = $"driver-support-issue:{request.IssueId:N}:resolved",
                PayloadJson = JsonSerializer.Serialize(new { IssueId = request.IssueId, RestaurantId = restaurantId.Value, DriverProfileId = driverId, Category = category, Event = "resolved" }),
                CreatedAtUtc = now
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        var successMessage = _messages.GetMessage(
            MessageCodes.DispatcherSupport.ResolveSuccess,
            isAr ? "تم حل المشكلة بنجاح" : "Issue resolved successfully");

        var resolvedByName = await _db.GetUserDisplayNameAsync(_currentUser!.UserId!.Value, cancellationToken);
        var resolutionDto = new SupportProblemResolutionDto(
            Notes: trimmedNotes,
            ResolvedAtUtc: now,
            ResolvedById: _currentUser.UserId,
            ResolvedByName: resolvedByName);

        return Result.Success(new ResolveSupportIssueResponseDto(
            IssueId: request.IssueId,
            Status: "Resolved",
            StatusLabel: isAr ? "تم الحل" : "Resolved",
            Resolution: resolutionDto,
            ResolvedAtUtc: now,
            Message: successMessage,
            DriverId: driverId));
    }
}
