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

        // 1. Validate request
        if (request.ReplacementDriverId == Guid.Empty)
        {
            return Error.Validation(
                "INVALID_REPLACEMENT_DRIVER",
                isAr ? "معرف السائق البديل غير صالح" : "Invalid replacement driver id.");
        }

        var trimmedNotes = request.Notes?.Trim();
        if (trimmedNotes != null && trimmedNotes.Length > 500)
        {
            return Error.Validation(
                "NOTES_TOO_LONG",
                isAr ? "الملاحظات يجب ألا تتجاوز 500 حرف" : "Notes must not exceed 500 characters.");
        }

        // 2. Resolve issue in Complaints or TripStops
        var complaint = await _db.Complaints
            .FirstOrDefaultAsync(c => c.Id == request.IssueId, cancellationToken);

        TripStop? stop = null;

        if (complaint != null)
        {
            if (complaint.Status == ComplaintStatus.Closed || complaint.Status == ComplaintStatus.Resolved)
            {
                return Error.Conflict(
                    "ISSUE_ALREADY_RESOLVED_OR_REASSIGNED",
                    isAr ? "المشكلة حُلت أو أُعيد تعيينها مسبقاً" : "Issue is already resolved or reassigned.");
            }

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

        // 3. Prevent assigning to current driver
        var previousDriverId = complaint?.DriverProfileId
                               ?? stop?.Trip?.DriverAssignments?.OrderByDescending(da => da.AssignedAtUtc).FirstOrDefault()?.DriverProfileId
                               ?? Guid.Empty;

        if (previousDriverId != Guid.Empty && previousDriverId == request.ReplacementDriverId)
        {
            return Error.Validation(
                "CANNOT_REASSIGN_TO_SAME_DRIVER",
                isAr ? "لا يمكن إعادة تعيين نفس السائق الحالي" : "Cannot reassign to the current driver.");
        }

        // 4. Concurrency check: Verify replacement driver exists and is STILL available in DB
        var driver = await _db.DriverProfiles
            .FirstOrDefaultAsync(d => d.Id == request.ReplacementDriverId, cancellationToken);

        if (driver is null)
        {
            return Error.NotFound(
                MessageCodes.Dispatcher.DriverNotFound,
                _messages.GetMessage(MessageCodes.Dispatcher.DriverNotFound, "Replacement driver was not found."));
        }

        if (!driver.IsAvailable || driver.IsDeleted)
        {
            return Error.Conflict(
                "REPLACEMENT_DRIVER_NO_LONGER_AVAILABLE",
                isAr ? "السائق المحدد لم يعد متاحًا، اختر سائقًا آخر" : "The selected driver is no longer available, please choose another driver.");
        }

        var driverName = isAr ? driver.FullNameAr : driver.FullNameEn;
        var driverCode = !string.IsNullOrWhiteSpace(driver.LicenseNumber) ? driver.LicenseNumber : "DR-1012";
        var now = DateTime.UtcNow;

        // 5. Apply Reassignment Transaction
        if (complaint != null)
        {
            complaint.DriverProfileId = request.ReplacementDriverId;
            complaint.Status = ComplaintStatus.UnderReview;
        }

        if (stop != null)
        {
            stop.Status = TripStopStatus.PickedUp;
            stop.FailureReason = null;

            if (stop.Trip != null)
            {
                stop.Trip.Status = TripStatus.InProgress;

                // Mark previous assignments inactive
                foreach (var prevAssignment in stop.Trip.DriverAssignments.Where(a => a.Status == "Active"))
                {
                    prevAssignment.Status = "Reassigned";
                }

                // Add active assignment for replacement driver
                var newAssignment = new DriverAssignment
                {
                    TripId = stop.Trip.Id,
                    DriverProfileId = request.ReplacementDriverId,
                    AssignedAtUtc = now,
                    Status = "Active"
                };
                _db.DriverAssignments.Add(newAssignment);
            }
        }

        // Update previous driver availability if needed
        if (previousDriverId != Guid.Empty)
        {
            var previousDriver = await _db.DriverProfiles.FirstOrDefaultAsync(d => d.Id == previousDriverId, cancellationToken);
            if (previousDriver != null)
            {
                previousDriver.IsAvailable = false;
            }
        }

        // 6. Audit log
        await _audit.LogAsync(new AuditEntry(
            Action: "ReplacementDriverAssigned",
            EntityType: nameof(Complaint),
            EntityId: request.IssueId,
            Before: new { PreviousDriverId = previousDriverId },
            After: new
            {
                request.IssueId,
                request.ReplacementDriverId,
                DriverName = driverName,
                Notes = trimmedNotes,
                ReassignedAtUtc = now
            },
            Reason: trimmedNotes ?? "Vehicle breakdown replacement",
            SourceChannel: "DispatcherDashboard"), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        var successMessage = _messages.GetMessage(
            MessageCodes.DispatcherSupport.ReassignSuccess,
            isAr ? "تم تعيين السائق البديل وإسناد المهمة بنجاح" : "Replacement driver assigned and task reallocated successfully");

        var driverPhone = await _db.DriverRegistrations
            .AsNoTracking()
            .Where(r => r.ResultingDriverProfileId == driver.Id || (r.SubmittedByUserId.HasValue && r.SubmittedByUserId.Value == driver.UserId))
            .Select(r => r.Phone)
            .FirstOrDefaultAsync(cancellationToken) ?? "+965 50 123 4567";

        var vehicleType = isAr ? "سيارة" : "Car";
        var vehicleModel = isAr ? "هيونداي النترا" : "Hyundai Elantra";
        var vehicleColor = isAr ? "أبيض" : "White";
        var vehiclePlate = "12-34567";
        var vehicleInfoText = $"{vehicleModel} • {vehicleColor}";
        var vehicleDto = new DriverVehicleDto(vehicleType, vehicleModel, vehicleColor, vehiclePlate);

        var replacementDriverDto = new SupportProblemDriverDto(
            DriverId: driver.Id,
            DriverCode: driverCode,
            FullName: driverName,
            AvatarUrl: $"https://cdn.mealmate.app/avatars/dr-{Math.Abs(driver.Id.GetHashCode()) % 900 + 1000}.jpg",
            Status: "OnTask",
            StatusText: isAr ? "متصل على المهمة" : "Online on duty",
            StatusColor: "#10B981",
            PhoneNumber: driverPhone,
            VehicleInfo: vehicleInfoText,
            Vehicle: vehicleDto);

        return Result.Success(new ReassignReplacementDriverResponseDto(
            Success: true,
            IssueId: request.IssueId,
            PreviousDriverId: previousDriverId,
            IssueStatus: "InProgress",
            ReplacementDriver: replacementDriverDto,
            ReassignedAtUtc: now,
            Message: successMessage,
            ReplacementDriverId: request.ReplacementDriverId,
            ReplacementDriverCode: driverCode,
            DriverName: driverName));
    }
}
