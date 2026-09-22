using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

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

    public ResolveSupportIssueCommandHandler(
        IAppDbContext db,
        ILocalizedMessageProvider messages,
        IAuditLogger audit,
        ICurrentUser? currentUser = null)
    {
        _db = db;
        _messages = messages;
        _audit = audit;
        _currentUser = currentUser;
    }

    public async Task<Result<ResolveSupportIssueResponseDto>> Handle(
        ResolveSupportIssueCommand request,
        CancellationToken cancellationToken)
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

        // 2. Fetch complaint or stop
        var complaint = await _db.Complaints
            .Include(c => c.AdminReviews)
            .FirstOrDefaultAsync(c => c.Id == request.IssueId, cancellationToken);

        Guid? driverId = null;
        if (complaint != null)
        {
            if (complaint.Status == ComplaintStatus.Resolved || complaint.Status == ComplaintStatus.Closed)
            {
                return Error.Conflict(
                    "ISSUE_ALREADY_RESOLVED",
                    isAr ? "تم حل المشكلة بالفعل" : "Issue is already resolved.");
            }

            driverId = complaint.DriverProfileId;
            complaint.Status = ComplaintStatus.Resolved;
        }
        else
        {
            var stop = await _db.TripStops
                .FirstOrDefaultAsync(s => s.Id == request.IssueId, cancellationToken);

            if (stop is null)
            {
                return Error.NotFound(
                    MessageCodes.DispatcherSupport.IssueNotFound,
                    _messages.GetMessage(MessageCodes.DispatcherSupport.IssueNotFound, "Support issue was not found."));
            }

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

            stop.FailureReason = null;
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
                ResolvedById = _currentUser?.UserId
            },
            Reason: trimmedNotes,
            SourceChannel: "DispatcherDashboard"), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        var successMessage = _messages.GetMessage(
            MessageCodes.DispatcherSupport.ResolveSuccess,
            isAr ? "تم حل المشكلة بنجاح" : "Issue resolved successfully");

        return Result.Success(new ResolveSupportIssueResponseDto(
            IssueId: request.IssueId,
            ResolvedAtUtc: now,
            Message: successMessage,
            DriverId: driverId));
    }
}
