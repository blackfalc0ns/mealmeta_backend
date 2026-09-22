using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record ReassignReplacementDriverCommand(
    Guid IssueId,
    Guid ReplacementDriverId,
    string? Notes = null) : ICommand<ReassignReplacementDriverResponseDto>;

public sealed class ReassignReplacementDriverCommandHandler : ICommandHandler<ReassignReplacementDriverCommand, ReassignReplacementDriverResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IAuditLogger _audit;

    public ReassignReplacementDriverCommandHandler(
        IAppDbContext db,
        ILocalizedMessageProvider messages,
        IAuditLogger audit)
    {
        _db = db;
        _messages = messages;
        _audit = audit;
    }

    public async Task<Result<ReassignReplacementDriverResponseDto>> Handle(
        ReassignReplacementDriverCommand request,
        CancellationToken cancellationToken)
    {
        var isAr = _messages.IsArabic();

        // 1. Validate replacement driver exists
        var driver = await _db.DriverProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.ReplacementDriverId, cancellationToken);

        if (driver is null)
        {
            return Error.NotFound(
                MessageCodes.Dispatcher.DriverNotFound,
                _messages.GetMessage(MessageCodes.Dispatcher.DriverNotFound, "Replacement driver was not found."));
        }

        var driverName = isAr ? driver.FullNameAr : driver.FullNameEn;

        // 2. Resolve issue in Complaints or TripStops
        var complaint = await _db.Complaints
            .FirstOrDefaultAsync(c => c.Id == request.IssueId, cancellationToken);

        TripStop? stop = null;

        if (complaint != null)
        {
            complaint.DriverProfileId = request.ReplacementDriverId;
            complaint.Status = ComplaintStatus.UnderReview;

            if (complaint.OrderId.HasValue || complaint.SubscriptionDayId.HasValue)
            {
                var boxId = complaint.OrderId ?? complaint.SubscriptionDayId;
                stop = await _db.TripStops
                    .Include(s => s.Trip)
                        .ThenInclude(t => t.DriverAssignments)
                    .FirstOrDefaultAsync(s => s.Id == boxId || s.SubscriptionDayId == boxId, cancellationToken);
            }
        }
        else
        {
            stop = await _db.TripStops
                .Include(s => s.Trip)
                    .ThenInclude(t => t.DriverAssignments)
                .FirstOrDefaultAsync(s => s.Id == request.IssueId, cancellationToken);

            if (stop is null)
            {
                return Error.NotFound(
                    MessageCodes.DispatcherSupport.IssueNotFound,
                    _messages.GetMessage(MessageCodes.DispatcherSupport.IssueNotFound, "Support issue was not found."));
            }
        }

        var now = DateTime.UtcNow;

        if (stop != null)
        {
            stop.Status = TripStopStatus.PickedUp;
            stop.FailureReason = null;

            if (stop.Trip != null)
            {
                stop.Trip.Status = TripStatus.InProgress;

                // Add or update DriverAssignment
                var assignment = new DriverAssignment
                {
                    TripId = stop.Trip.Id,
                    DriverProfileId = request.ReplacementDriverId,
                    AssignedAtUtc = now,
                    Status = "Active"
                };
                _db.DriverAssignments.Add(assignment);
            }
        }

        // 3. Audit log
        await _audit.LogAsync(new AuditEntry(
            Action: "ReplacementDriverAssigned",
            EntityType: nameof(Complaint),
            EntityId: request.IssueId,
            Before: null,
            After: new
            {
                request.IssueId,
                request.ReplacementDriverId,
                DriverName = driverName,
                request.Notes,
                ReassignedAtUtc = now
            },
            Reason: request.Notes ?? "Vehicle breakdown replacement",
            SourceChannel: "DispatcherDashboard"), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        var successMessage = _messages.GetMessage(
            MessageCodes.DispatcherSupport.ReassignSuccess,
            isAr ? "تم تعيين السائق البديل بنجاح" : "Replacement driver assigned successfully");

        return Result.Success(new ReassignReplacementDriverResponseDto(
            IssueId: request.IssueId,
            ReplacementDriverId: request.ReplacementDriverId,
            DriverName: driverName,
            ReassignedAtUtc: now,
            Message: successMessage));
    }
}
