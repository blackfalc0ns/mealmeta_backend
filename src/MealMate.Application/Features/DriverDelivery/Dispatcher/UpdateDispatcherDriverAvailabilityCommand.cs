using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record UpdateDispatcherDriverAvailabilityCommand(
    Guid DriverId,
    bool IsAvailable,
    string? Reason = null) : ICommand<UpdateDispatcherDriverAvailabilityResponseDto>;

public sealed record UpdateDispatcherDriverAvailabilityResponseDto(
    Guid DriverId,
    bool IsAvailable,
    string OperationalStatus,
    bool HasActiveAssignments,
    DateTime UpdatedAtUtc);

public sealed class UpdateDispatcherDriverAvailabilityCommandHandler
    : ICommandHandler<UpdateDispatcherDriverAvailabilityCommand, UpdateDispatcherDriverAvailabilityResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly ILocalizedMessageProvider _messages;

    public UpdateDispatcherDriverAvailabilityCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _messages = messages;
    }

    public async Task<Result<UpdateDispatcherDriverAvailabilityResponseDto>> Handle(
        UpdateDispatcherDriverAvailabilityCommand request,
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
                _messages.GetMessage("DRIVER_ACCESS_DENIED", "You do not have permission to manage driver availability."));
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

        // Check for active assignments & active trip stops
        var hasActiveAssignments = await _db.DriverAssignments
            .AsNoTracking()
            .Where(da => da.DriverProfileId == driver.Id && da.Status == "Active" &&
                         (da.Trip.Status == TripStatus.Assigned || da.Trip.Status == TripStatus.InProgress) &&
                         da.Trip.Stops.Any(s => s.Status == TripStopStatus.Pending || s.Status == TripStopStatus.PickedUp))
            .AnyAsync(cancellationToken);

        var previousAvailable = driver.IsAvailable;
        var nowUtc = DateTime.UtcNow;

        // Availability alone cannot activate an operationally Inactive driver:
        // the supervisor must use the shift-status command, which opens the work
        // session. Toggling availability to true without a session is a conflict.
        if (request.IsAvailable)
        {
            var hasActiveSession = await _db.DriverWorkSessions
                .AsNoTracking()
                .AnyAsync(s => s.DriverProfileId == driver.Id && s.Status == "Active", cancellationToken);

            if (!hasActiveSession)
            {
                var isArabic = _messages.IsArabic();
                return Error.Conflict(
                    "DRIVER_NOT_OPERATIONAL",
                    isArabic
                        ? "السائق غير مشغّل حاليًا (لا توجد جلسة عمل نشطة). فعّل السائق أولًا عبر أمر حالة التشغيل shift-status."
                        : "Driver is not operational (no active work session). Activate the driver via the shift-status command first.");
            }
        }

        // If idempotent (no change), return current state without creating duplicate audit entry
        if (previousAvailable == request.IsAvailable)
        {
            var opStatusCurrent = hasActiveAssignments
                ? "InDelivery"
                : (driver.IsAvailable ? "Available" : "Unavailable");

            return Result.Success(new UpdateDispatcherDriverAvailabilityResponseDto(
                DriverId: driver.Id,
                IsAvailable: driver.IsAvailable,
                OperationalStatus: opStatusCurrent,
                HasActiveAssignments: hasActiveAssignments,
                UpdatedAtUtc: driver.ModifiedAtUtc ?? nowUtc));
        }

        // Availability alone cannot activate an operationally Inactive driver:
        // the supervisor must use the shift-status command, which opens the work
        // session. Toggling availability to true without a session is a conflict.
        if (request.IsAvailable)
        {
            var hasActiveSession = await _db.DriverWorkSessions
                .AsNoTracking()
                .AnyAsync(s => s.DriverProfileId == driver.Id && s.Status == "Active", cancellationToken);

            if (!hasActiveSession)
            {
                var isArabic = _messages.IsArabic();
                return Error.Conflict(
                    "DRIVER_NOT_OPERATIONAL",
                    isArabic
                        ? "السائق غير مشغّل حاليًا (لا توجد جلسة عمل نشطة). فعّل السائق أولًا عبر أمر حالة التشغيل shift-status."
                        : "Driver is not operational (no active work session). Activate the driver via the shift-status command first.");
            }
        }

        // Apply state change
        driver.IsAvailable = request.IsAvailable;
        driver.ModifiedAtUtc = nowUtc;

        // Write append-only system audit log
        await _audit.LogAsync(new AuditEntry(
            Action: "UpdateAvailability",
            EntityType: nameof(DriverProfile),
            EntityId: driver.Id,
            Before: new { isAvailable = previousAvailable },
            After: new { isAvailable = request.IsAvailable },
            Reason: request.Reason,
            SourceChannel: "DispatcherApp"), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        var operationalStatus = hasActiveAssignments
            ? "InDelivery"
            : (driver.IsAvailable ? "Available" : "Unavailable");

        return Result.Success(new UpdateDispatcherDriverAvailabilityResponseDto(
            DriverId: driver.Id,
            IsAvailable: driver.IsAvailable,
            OperationalStatus: operationalStatus,
            HasActiveAssignments: hasActiveAssignments,
            UpdatedAtUtc: nowUtc));
    }
}
