using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record ValidatePickupBarcodeCommand(
    string BarcodeValue,
    bool IsManualEntry = false) : ICommand<ValidatePickupBarcodeResponseDto>;

public sealed class ValidatePickupBarcodeCommandHandler
    : ICommandHandler<ValidatePickupBarcodeCommand, ValidatePickupBarcodeResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public ValidatePickupBarcodeCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<ValidatePickupBarcodeResponseDto>> Handle(
        ValidatePickupBarcodeCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized(
                MessageCodes.Auth.InvalidCredentials,
                _messages.GetMessage(MessageCodes.Auth.InvalidCredentials, "Authentication is required."));
        }

        var driver = await _db.DriverProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == _currentUser.UserId.Value, cancellationToken);

        if (driver is null)
        {
            return Error.NotFound(
                "DRIVER_NOT_FOUND",
                _messages.GetMessage("DRIVER_NOT_FOUND", "Driver profile was not found."));
        }

        if (string.IsNullOrWhiteSpace(request.BarcodeValue))
        {
            return Error.Validation(
                "BARCODE_REQUIRED",
                _messages.GetMessage("BARCODE_REQUIRED", "Barcode value is required."));
        }

        var cleanInput = request.BarcodeValue.Trim().TrimStart('#');

        // Locate the driver's active assignments — a driver can hold several active
        // trips at once (the dispatcher adds boxes to an existing older trip or
        // opens a new one), so the scanned box may legitimately sit on any of
        // them, not just the newest.
        var activeAssignments = await _db.DriverAssignments
            .AsNoTracking()
            .Where(da => da.DriverProfileId == driver.Id &&
                         da.Status == "Active" &&
                         (da.Trip.Status == TripStatus.Assigned || da.Trip.Status == TripStatus.InProgress))
            .ToListAsync(cancellationToken);

        if (activeAssignments.Count == 0)
        {
            return Error.Validation(
                "BOX_NOT_IN_ACTIVE_TRIP",
                _messages.GetMessage("BOX_NOT_IN_ACTIVE_TRIP", "You do not have an active trip assigned."));
        }

        // 1. Search canonical MealBarcode (stored values carry a "#BX-" prefix; scanned/manual input may omit it)
        var mealBarcode = await _db.MealBarcodes
            .FirstOrDefaultAsync(b => b.BarcodeValue == cleanInput || b.BarcodeValue == "#" + cleanInput, cancellationToken);

        TripStop? matchedStop = null;

        if (mealBarcode != null)
        {
            if (mealBarcode.Status == MealBarcodeStatus.Revoked || mealBarcode.IsRevoked)
            {
                return Error.Conflict(
                    "BARCODE_REVOKED",
                    _messages.GetMessage("BARCODE_REVOKED", "This barcode has been revoked."));
            }

            matchedStop = await _db.TripStops
                .Include(s => s.Trip)
                .FirstOrDefaultAsync(s => s.MealBarcodeId == mealBarcode.Id ||
                                          (s.SubscriptionDayId.HasValue && s.SubscriptionDayId.Value == mealBarcode.SubscriptionDayId),
                                     cancellationToken);

            if (matchedStop is null)
            {
                return Error.NotFound(
                    "BARCODE_NOT_FOUND",
                    _messages.GetMessage("BARCODE_NOT_FOUND", "Barcode not found in the system."));
            }

            if (!matchedStop.MealBarcodeId.HasValue)
            {
                matchedStop.MealBarcodeId = mealBarcode.Id;
            }
        }
        else
        {
            // Fallback for human-entered order codes
            matchedStop = await _db.TripStops
                .Include(s => s.Trip)
                .FirstOrDefaultAsync(s => s.OrderCode != null &&
                                          (s.OrderCode.Equals(cleanInput, StringComparison.OrdinalIgnoreCase) ||
                                           s.OrderCode.EndsWith(cleanInput, StringComparison.OrdinalIgnoreCase) ||
                                           cleanInput.EndsWith(s.OrderCode, StringComparison.OrdinalIgnoreCase)),
                                     cancellationToken);

            if (matchedStop is null)
            {
                return Error.NotFound(
                    "BARCODE_NOT_FOUND",
                    _messages.GetMessage("BARCODE_NOT_FOUND", "Barcode not found in the system."));
            }
        }

        // Verify the stop belongs to one of the driver's active trips
        var stopAssignment = activeAssignments.FirstOrDefault(da => da.TripId == matchedStop.TripId);
        if (stopAssignment is null)
        {
            return Error.Forbidden(
                "BOX_NOT_ASSIGNED_TO_DRIVER",
                _messages.GetMessage("BOX_NOT_ASSIGNED_TO_DRIVER", "This box is not assigned to your active trip."));
        }

        // Check if already picked up
        if (matchedStop.Status == TripStopStatus.PickedUp || matchedStop.Status == TripStopStatus.Delivered)
        {
            return Error.Conflict(
                "BOX_ALREADY_PICKED_UP",
                _messages.GetMessage("BOX_ALREADY_PICKED_UP", "This box has already been picked up."));
        }

        // Generate opaque random token and hash
        var rawTokenBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToHexString(rawTokenBytes).ToLowerInvariant();
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();

        var nowUtc = DateTime.UtcNow;
        var expiresAtUtc = nowUtc.AddMinutes(5);

        var targetBoxId = matchedStop.SubscriptionDayId ?? matchedStop.Id;

        var validation = new PickupValidation
        {
            TripId = stopAssignment.TripId,
            BoxId = targetBoxId,
            DriverProfileId = driver.Id,
            DriverAssignmentId = stopAssignment.Id,
            BarcodeValue = cleanInput,
            TokenHash = tokenHash,
            ValidatedAtUtc = nowUtc,
            ExpiresAtUtc = expiresAtUtc,
            IsManualEntry = request.IsManualEntry
        };

        _db.PickupValidations.Add(validation);
        await _db.SaveChangesAsync(cancellationToken);

        var isAr = _messages.IsArabic();
        var boxDto = new ValidatePickupBoxDto(
            BoxId: targetBoxId,
            BoxCode: matchedStop.OrderCode ?? cleanInput,
            CustomerName: matchedStop.CustomerName ?? (isAr ? "العميل" : "Customer"),
            DeliveryZone: (isAr ? matchedStop.ZoneNameAr : matchedStop.ZoneNameEn) ?? string.Empty,
            MealsCount: matchedStop.BoxCount > 0 ? matchedStop.BoxCount : 1);

        return Result.Success(new ValidatePickupBarcodeResponseDto(
            IsValid: true,
            ValidationToken: rawToken,
            ExpiresAtUtc: expiresAtUtc,
            Box: boxDto,
            ScanStatus: "BarcodeValidated",
            NextAction: "CaptureConditionPhoto"));
    }
}
