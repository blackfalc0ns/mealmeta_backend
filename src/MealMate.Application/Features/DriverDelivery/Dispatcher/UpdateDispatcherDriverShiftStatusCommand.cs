using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record UpdateDispatcherDriverShiftStatusCommand(
    Guid DriverId,
    string ShiftStatus,
    string? Reason = null) : ICommand<UpdateDispatcherDriverShiftStatusResponseDto>;

public sealed record UpdateDispatcherDriverShiftStatusResponseDto(
    Guid DriverId,
    string ShiftStatus,
    bool IsAvailable,
    string CurrentStatusText,
    DateTime UpdatedAtUtc);

/// <summary>
/// Delivery-supervisor control of a driver's operational status. "Active" opens a
/// single work session after the standing eligibility checks; "Inactive" closes the
/// session and clears availability, and is rejected with a conflict while the
/// driver still holds active delivery stops. Availability toggles cannot activate
/// an inactive driver, so this command is the only path into operational status.
/// </summary>
public sealed class UpdateDispatcherDriverShiftStatusCommandHandler
    : ICommandHandler<UpdateDispatcherDriverShiftStatusCommand, UpdateDispatcherDriverShiftStatusResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IUserAccountService? _users;

    public UpdateDispatcherDriverShiftStatusCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        ILocalizedMessageProvider messages,
        IUserAccountService? users = null)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _messages = messages;
        _users = users;
    }

    public async Task<Result<UpdateDispatcherDriverShiftStatusResponseDto>> Handle(
        UpdateDispatcherDriverShiftStatusCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Error.Unauthorized(
                MessageCodes.Auth.InvalidCredentials,
                _messages.GetMessage(MessageCodes.Auth.InvalidCredentials, "Authentication is required."));
        }

        var isDeliveryManager = _currentUser.IsInRole(AppRoles.DeliveryManager) ||
                                _currentUser.IsInRole(AppRoles.RestaurantOwner) ||
                                _currentUser.IsInRole(AppRoles.SuperAdmin) ||
                                _currentUser.IsInRole(AppRoles.OperationsAdmin);

        if (!isDeliveryManager && !_currentUser.HasPermission(PermissionCodes.DeliveryDriversManage))
        {
            return Error.Forbidden(
                "DRIVER_ACCESS_DENIED",
                _messages.GetMessage("DRIVER_ACCESS_DENIED", "You do not have permission to manage driver operational status."));
        }

        var restaurantId = _currentUser.RestaurantId;

        // Query driver within the authorized restaurant boundary
        var query = _db.DriverProfiles.Where(d => d.Id == request.DriverId);
        if (restaurantId.HasValue && restaurantId.Value != Guid.Empty)
        {
            query = query.Where(d => d.RestaurantId == restaurantId.Value);
        }

        var driver = await query.FirstOrDefaultAsync(cancellationToken);
        if (driver is null)
        {
            return Error.NotFound(
                "DRIVER_NOT_FOUND",
                _messages.GetMessage("DRIVER_NOT_FOUND", "Driver was not found in the authorized restaurant."));
        }

        var isAr = _messages.IsArabic();
        var nowUtc = DateTime.UtcNow;

        var normalizedStatus = request.ShiftStatus?.Trim();
        var wantsActive = string.Equals(normalizedStatus, DriverOperationalStateResolver.StatusActive, StringComparison.OrdinalIgnoreCase);
        var wantsInactive = string.Equals(normalizedStatus, DriverOperationalStateResolver.StatusInactive, StringComparison.OrdinalIgnoreCase);

        if (!wantsActive && !wantsInactive)
        {
            return Error.Validation(
                "DRIVER_SHIFT_STATUS_INVALID",
                isAr
                    ? "حالة التشغيل يجب أن تكون Active أو Inactive فقط."
                    : "Shift status must be 'Active' or 'Inactive'.");
        }

        var activeSession = await _db.DriverWorkSessions
            .FirstOrDefaultAsync(s => s.DriverProfileId == driver.Id && s.Status == "Active", cancellationToken);

        var previousShiftStatus = activeSession is null
            ? DriverOperationalStateResolver.StatusInactive
            : DriverOperationalStateResolver.StatusActive;
        var previousAvailability = driver.IsAvailable;

        if (wantsActive)
        {
            if (activeSession is not null && driver.IsAvailable)
            {
                // Idempotent repeat: never create a second session and never move
                // the original start time.
                return Success(driver, activeSession, nowUtc);
            }

            if (activeSession is null)
            {
                var eligibility = await DriverOperationalEligibility.CheckEligibilityAsync(
                    _db, _users, driver, nowUtc, isAr, cancellationToken: cancellationToken);
                if (eligibility.IsFailure)
                {
                    return eligibility.Error;
                }

                activeSession = new DriverWorkSession
                {
                    DriverProfileId = driver.Id,
                    RestaurantId = driver.RestaurantId,
                    StartedAtUtc = nowUtc,
                    Status = "Active",
                    ShiftLabel = isAr ? "وردية بدأها مسؤول التوصيل" : "Supervisor-initiated shift",
                    DailyTargetOrders = 8,
                    Notes = request.Reason
                };
                _db.DriverWorkSessions.Add(activeSession);
            }

            driver.IsAvailable = true;
        }
        else
        {
            if (activeSession is not null)
            {
                // An operating driver with undelivered stops cannot be stopped.
                var pendingStopsCount = await (
                    from stop in _db.TripStops
                    join da in _db.DriverAssignments on stop.TripId equals da.TripId
                    where da.DriverProfileId == driver.Id
                          && da.Status == "Active"
                          && (stop.Status == TripStopStatus.Pending || stop.Status == TripStopStatus.PickedUp)
                    select stop.Id
                ).CountAsync(cancellationToken);

                if (pendingStopsCount > 0)
                {
                    return new Error(
                        "DRIVER_SHIFT_ACTIVE_ORDERS",
                        isAr
                            ? $"لا يمكن إيقاف السائق لوجود {pendingStopsCount} طلبات نشطة لم تُسلَّم بعد. أكمل الطلبات أو أعد إسنادها أولًا."
                            : $"Cannot stop the driver while {pendingStopsCount} active deliveries remain. Complete or reassign them first.",
                        ErrorType.Conflict,
                        new object?[] { pendingStopsCount });
                }

                var today = DateOnly.FromDateTime(nowUtc);
                var deliveredCount = await (
                    from stop in _db.TripStops
                    join da in _db.DriverAssignments on stop.TripId equals da.TripId
                    where da.DriverProfileId == driver.Id
                          && stop.Status == TripStopStatus.Delivered
                          && stop.Trip.PlannedDate == today
                    select stop.Id
                ).CountAsync(cancellationToken);

                activeSession.Status = "Ended";
                activeSession.EndedAtUtc = nowUtc;
                if (deliveredCount > 0)
                {
                    activeSession.CompletedOrdersCount = deliveredCount;
                }
                if (!string.IsNullOrWhiteSpace(request.Reason))
                {
                    activeSession.Notes = string.IsNullOrWhiteSpace(activeSession.Notes)
                        ? request.Reason
                        : $"{activeSession.Notes} | {request.Reason}";
                }
            }

            driver.IsAvailable = false;
        }

        driver.ModifiedAtUtc = nowUtc;

        await _audit.LogAsync(new AuditEntry(
            Action: "UpdateShiftStatus",
            EntityType: nameof(DriverProfile),
            EntityId: driver.Id,
            Before: new { shiftStatus = previousShiftStatus, isAvailable = previousAvailability },
            After: new
            {
                shiftStatus = wantsActive ? DriverOperationalStateResolver.StatusActive : DriverOperationalStateResolver.StatusInactive,
                isAvailable = wantsActive
            },
            Reason: request.Reason,
            SourceChannel: "DispatcherApp"), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return Success(driver, wantsActive ? activeSession : null, nowUtc);
    }

    private Result<UpdateDispatcherDriverShiftStatusResponseDto> Success(
        DriverProfile driver,
        DriverWorkSession? activeSession,
        DateTime nowUtc)
    {
        var state = DriverOperationalStateResolver.Resolve(driver, activeSession, _messages.IsArabic());
        return Result.Success(new UpdateDispatcherDriverShiftStatusResponseDto(
            DriverId: driver.Id,
            ShiftStatus: state.ShiftStatus,
            IsAvailable: state.IsAvailable,
            CurrentStatusText: state.CurrentStatusText,
            UpdatedAtUtc: nowUtc));
    }
}
