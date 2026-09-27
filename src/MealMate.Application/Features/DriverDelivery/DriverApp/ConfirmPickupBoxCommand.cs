using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record ConfirmPickupBoxCommand(
    Guid BoxId,
    string ValidationToken,
    string ConditionPhotoStorageKey,
    decimal Latitude,
    decimal Longitude,
    string IdempotencyKey) : ICommand<ConfirmPickupBoxResponseDto>;

public sealed class ConfirmPickupBoxCommandHandler
    : ICommandHandler<ConfirmPickupBoxCommand, ConfirmPickupBoxResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public ConfirmPickupBoxCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<ConfirmPickupBoxResponseDto>> Handle(
        ConfirmPickupBoxCommand request,
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
            return Error.NotFound("DRIVER_NOT_FOUND", _messages.GetMessage("DRIVER_NOT_FOUND", "Driver profile not found."));
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Error.Validation("IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key header is required.");
        }

        var requestHash = ComputeRequestHash(request);
        var idempotencyScope = $"PickupConfirm:{driver.Id}";

        // 1. Idempotency Check
        var existingRecord = await _db.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Scope == idempotencyScope && r.Key == request.IdempotencyKey, cancellationToken);

        if (existingRecord != null)
        {
            if (existingRecord.RequestHash != requestHash)
            {
                return Error.Conflict("IDEMPOTENCY_KEY_REUSED", "An operation with this idempotency key already exists with different request parameters.");
            }

            if (!string.IsNullOrWhiteSpace(existingRecord.ResponseBody))
            {
                var cached = JsonSerializer.Deserialize<ConfirmPickupBoxResponseDto>(existingRecord.ResponseBody);
                if (cached != null) return Result.Success(cached);
            }
        }

        if (request.Latitude == 0 && request.Longitude == 0)
        {
            return Error.Validation("LOCATION_REQUIRED", "GPS coordinates are required to confirm pickup.");
        }

        if (string.IsNullOrWhiteSpace(request.ValidationToken))
        {
            return Error.Validation("VALIDATION_TOKEN_INVALID", "Validation token is required.");
        }

        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.ValidationToken.Trim()))).ToLowerInvariant();

        var validation = await _db.PickupValidations
            .FirstOrDefaultAsync(v => v.BoxId == request.BoxId &&
                                      v.DriverProfileId == driver.Id &&
                                      v.TokenHash == tokenHash, cancellationToken);

        if (validation is null)
        {
            return Error.Validation("VALIDATION_TOKEN_INVALID", "Invalid validation token.");
        }

        if (validation.ExpiresAtUtc < DateTime.UtcNow)
        {
            return Error.Validation("VALIDATION_TOKEN_EXPIRED", "Validation token has expired.");
        }

        if (validation.ConsumedAtUtc.HasValue)
        {
            return Error.Validation("VALIDATION_TOKEN_CONSUMED", "Validation token has already been consumed.");
        }

        var activeAssignmentExists = await _db.DriverAssignments
            .AsNoTracking()
            .AnyAsync(da => da.Id == validation.DriverAssignmentId && da.Status == "Active", cancellationToken);

        if (!activeAssignmentExists)
        {
            return Error.Forbidden("BOX_ASSIGNMENT_CHANGED", "Driver assignment for this trip has changed.");
        }

        var photo = await _db.PickupConditionPhotos
            .FirstOrDefaultAsync(p => p.PickupValidationId == validation.Id &&
                                      p.BoxId == request.BoxId &&
                                      p.StorageKey == request.ConditionPhotoStorageKey &&
                                      p.DeletedAtUtc == null, cancellationToken);

        if (photo is null)
        {
            return Error.Validation("PHOTO_NOT_UPLOADED", "No condition photo record found matching this storage key and validation.");
        }

        var stop = await _db.TripStops
            .FirstOrDefaultAsync(s => s.Id == request.BoxId && s.TripId == validation.TripId, cancellationToken);

        if (stop is null)
        {
            return Error.NotFound("BOX_NOT_FOUND", "Stop record not found.");
        }

        if (stop.Status == TripStopStatus.PickedUp || stop.Status == TripStopStatus.Delivered)
        {
            return Error.Conflict("BOX_ALREADY_PICKED_UP", "Box is already marked as picked up.");
        }

        var nowUtc = DateTime.UtcNow;

        // Execute state mutations
        validation.ConsumedAtUtc = nowUtc;
        photo.ConfirmedAtUtc = nowUtc;
        stop.Status = TripStopStatus.PickedUp;

        var pickupScan = new PickupScan
        {
            TripId = validation.TripId,
            OrderId = stop.DailyOrderId,
            SubscriptionDayId = stop.SubscriptionDayId,
            DriverProfileId = driver.Id,
            BarcodeValue = validation.BarcodeValue,
            PhotoPath = photo.StorageKey,
            ScannedAtUtc = nowUtc,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            IsManualEntry = validation.IsManualEntry
        };
        _db.PickupScans.Add(pickupScan);

        await _db.SaveChangesAsync(cancellationToken);

        // Derive authoritative counts
        var tripStops = await _db.TripStops
            .AsNoTracking()
            .Where(s => s.TripId == validation.TripId)
            .ToListAsync(cancellationToken);

        var assignedBoxesCount = tripStops.Count;
        var receivedBoxesCount = tripStops.Count(s => s.Status == TripStopStatus.PickedUp || s.Status == TripStopStatus.Delivered);
        var remainingBoxesCount = Math.Max(0, assignedBoxesCount - receivedBoxesCount);
        var allBoxesReceived = remainingBoxesCount == 0 && assignedBoxesCount > 0;

        var validatedBoxesCount = await _db.PickupValidations
            .AsNoTracking()
            .Where(v => v.TripId == validation.TripId && v.DriverAssignmentId == validation.DriverAssignmentId)
            .Select(v => v.BoxId)
            .Distinct()
            .CountAsync(cancellationToken);

        var nextAction = allBoxesReceived ? "ShowPickupSummary" : "ShowBoxSuccess";

        var response = new ConfirmPickupBoxResponseDto(
            TripId: validation.TripId,
            BoxId: stop.Id,
            BoxCode: stop.OrderCode ?? string.Empty,
            Status: "PickedUp",
            PickedUpAtUtc: nowUtc,
            AssignedBoxesCount: assignedBoxesCount,
            ValidatedBoxesCount: validatedBoxesCount,
            ReceivedBoxesCount: receivedBoxesCount,
            RemainingBoxesCount: remainingBoxesCount,
            AllBoxesReceived: allBoxesReceived,
            NextAction: nextAction);

        // Record Idempotency outcome
        var newRecord = new IdempotencyRecord
        {
            Scope = idempotencyScope,
            Key = request.IdempotencyKey,
            RequestHash = requestHash,
            ResponseBody = JsonSerializer.Serialize(response),
            HttpStatusCode = 200,
            UserId = driver.UserId,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.AddHours(24)
        };
        _db.IdempotencyRecords.Add(newRecord);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(response);
    }

    private static string ComputeRequestHash(ConfirmPickupBoxCommand request)
    {
        var raw = $"{request.BoxId}:{request.ValidationToken}:{request.ConditionPhotoStorageKey}:{request.Latitude}:{request.Longitude}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}
