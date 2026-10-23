using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using MealMate.Application.Common.Testing;
using OutboxMessage = MealMate.Domain.Common.OutboxMessage;

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
    private readonly IApplicationTransactionExecutor? _transactionExecutor;
    private readonly IApplicationLockManager? _locks;
    private readonly IOrderLifecycleFailureInjector _failureInjector;
    private readonly IRestaurantDeliveryNotifier? _restaurantNotifier;
    private readonly MealMate.Application.Common.Storage.SupportEvidenceScanService? _evidenceScans;

    public ConfirmPickupBoxCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages,
        IApplicationTransactionExecutor? transactionExecutor = null,
        IOrderLifecycleFailureInjector? failureInjector = null,
        IRestaurantDeliveryNotifier? restaurantNotifier = null,
        IApplicationLockManager? locks = null,
        MealMate.Application.Common.Storage.SupportEvidenceScanService? evidenceScans = null)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
        _transactionExecutor = transactionExecutor;
        _locks = locks;
        _failureInjector = failureInjector ?? NullOrderLifecycleFailureInjector.Instance;
        _restaurantNotifier = restaurantNotifier;
        _evidenceScans = evidenceScans;
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
            .FirstOrDefaultAsync(d => d.UserId == _currentUser.UserId.Value && !d.IsDeleted, cancellationToken);

        if (driver is null)
        {
            return Error.NotFound("DRIVER_NOT_FOUND", _messages.GetMessage("DRIVER_NOT_FOUND", "Driver profile not found."));
        }

        if (_locks is null || _transactionExecutor is null)
            return Error.Conflict("PICKUP_INFRASTRUCTURE_UNAVAILABLE", _messages.GetMessage("driver_support_handover.transaction_unavailable", "Pickup requires a transaction and application locks."));
        if (!(await _db.GetActiveAccountUserIdsAsync(new[] { driver.UserId }, cancellationToken)).Contains(driver.UserId))
            return Error.Forbidden("DRIVER_ACCOUNT_INACTIVE", _messages.GetMessage("driver.account_inactive", "The driver account is not active."));

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

        var requestedTripId = await _db.TripStops.AsNoTracking()
            .Where(s => s.Id == request.BoxId || s.SubscriptionDayId == request.BoxId)
            .Select(s => (Guid?)s.TripId).FirstOrDefaultAsync(cancellationToken);
        if (!requestedTripId.HasValue) return Error.NotFound("BOX_NOT_FOUND", _messages.GetMessage("Orders.NotFound", "Stop record not found."));
        await _locks.LockTripAsync(requestedTripId.Value, cancellationToken);
        await _locks.LockDriverAsync(driver.Id, cancellationToken);
        var currentTripId = await _db.TripStops.AsNoTracking()
            .Where(s => s.Id == request.BoxId || s.SubscriptionDayId == request.BoxId)
            .Select(s => (Guid?)s.TripId).FirstOrDefaultAsync(cancellationToken);
        if (currentTripId != requestedTripId) return Error.Conflict("DELIVERY_CUSTODY_CHANGED", _messages.GetMessage("driver_support_handover.custody_changed", "The box changed trips while this request was waiting. Reload delivery state."));
        var requestedTripStatus = await _db.Trips.AsNoTracking().Where(x => x.Id == requestedTripId.Value).Select(x => x.Status).FirstOrDefaultAsync(cancellationToken);
        if (requestedTripStatus is TripStatus.Completed or TripStatus.Cancelled)
            return Error.Conflict("TRIP_TERMINAL", _messages.GetMessage("driver_support_handover.trip_terminal", "A terminal trip cannot accept pickup mutations."));
        if (await DriverSupportHandoverMutationGuard.HasActiveHandoverForTripAsync(_db, requestedTripId.Value, cancellationToken))
            return Error.Conflict("HANDOVER_PENDING", _messages.GetMessage("driver_support_handover.pending", "The active support handover must be completed or cancelled first."));
        var canonicalStopId = await _db.TripStops.AsNoTracking().Where(x => x.TripId == requestedTripId.Value
                && (x.Id == request.BoxId || x.SubscriptionDayId == request.BoxId))
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
        if (!canonicalStopId.HasValue) return Error.NotFound("BOX_NOT_FOUND", _messages.GetMessage("Orders.NotFound", "Stop record not found."));
        if (await DriverSupportHandoverMutationGuard.HasActiveHoldForStopAsync(_db, canonicalStopId.Value, cancellationToken))
            return Error.Conflict("DELIVERY_HOLD_ACTIVE", _messages.GetMessage("driver_support_handover.followup_active", "This box has an unresolved support follow-up."));

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
            .AnyAsync(da => da.Id == validation.DriverAssignmentId && da.DriverProfileId == driver.Id
                && da.TripId == validation.TripId && da.Status == "Active", cancellationToken);

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

        var upload = await _db.UploadFileRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.StorageKey == photo.StorageKey, cancellationToken);
        if (upload is null || upload.UploadedByUserId != _currentUser.UserId || upload.Scope != "drivers"
            || upload.Folder != "delivery" || upload.ContentType != photo.ContentType)
            return Error.Validation("PHOTO_UPLOAD_INVALID", "The condition photo does not match an owned driver evidence upload.");
        if (_evidenceScans is null) return Error.Failure("UPLOAD_EVIDENCE_SCAN_FAILED", "Evidence scanning is unavailable.");
        var scanned = await _evidenceScans.ReadCleanAsync(upload, cancellationToken);
        if (scanned.Bytes is null || !MealMate.Application.Features.UnifiedSupport.SupportCaseService.HasValidSignature(scanned.Bytes, upload.ContentType))
            return Error.Validation("PHOTO_UPLOAD_INVALID", "The condition photo could not be verified as clean.");

        var stop = await _db.TripStops
            .FirstOrDefaultAsync(s => (s.Id == request.BoxId || s.SubscriptionDayId == request.BoxId) && s.TripId == validation.TripId, cancellationToken);

        if (stop is null)
        {
            return Error.NotFound("BOX_NOT_FOUND", "Stop record not found.");
        }

        if (stop.Status == TripStopStatus.PickedUp || stop.Status == TripStopStatus.Delivered)
        {
            return Error.Conflict("BOX_ALREADY_PICKED_UP", "Box is already marked as picked up.");
        }

        var nowUtc = DateTime.UtcNow;

        // Locate linked SubscriptionDay and RestaurantDailyOrder
        SubscriptionDay? linkedDay = null;
        if (stop.SubscriptionDayId.HasValue)
        {
            linkedDay = await _db.SubscriptionDays
                .FirstOrDefaultAsync(d => d.Id == stop.SubscriptionDayId.Value, cancellationToken);
        }

        RestaurantDailyOrder? linkedDailyOrder = null;
        if (stop.DailyOrderId.HasValue)
        {
            linkedDailyOrder = await _db.RestaurantDailyOrders
                .FirstOrDefaultAsync(o => o.Id == stop.DailyOrderId.Value, cancellationToken);
        }
        else if (stop.SubscriptionDayId.HasValue)
        {
            linkedDailyOrder = await _db.RestaurantDailyOrders
                .FirstOrDefaultAsync(o => o.SubscriptionDayId == stop.SubscriptionDayId.Value, cancellationToken);
        }

        ConfirmPickupBoxResponseDto? response = null;

        async Task ExecutePickupMutationsAsync(CancellationToken ct)
        {
            validation.ConsumedAtUtc = nowUtc;
            await _failureInjector.ExecuteAsync("AfterValidationConsumed", ct);

            photo.ConfirmedAtUtc = nowUtc;

            stop.Status = TripStopStatus.PickedUp;
            await _failureInjector.ExecuteAsync("AfterStopUpdated", ct);

            if (linkedDay != null)
            {
                linkedDay.Status = SubscriptionDayStatus.OutForDelivery;
                await _failureInjector.ExecuteAsync("AfterDayUpdated", ct);
            }

            if (linkedDailyOrder != null)
            {
                linkedDailyOrder.Status = OrderStatus.OnTheWay;
                linkedDailyOrder.ModifiedAtUtc = nowUtc;
                await _failureInjector.ExecuteAsync("AfterOrderUpdated", ct);
            }

            var handover = new OrderHandover
            {
                DailyOrderId = linkedDailyOrder?.Id ?? (stop.DailyOrderId ?? Guid.Empty),
                TripId = validation.TripId,
                TripStopId = stop.Id,
                MealBarcodeId = stop.MealBarcodeId,
                DriverProfileId = driver.Id,
                AttemptNumber = 1,
                HandedOverAtUtc = nowUtc,
                Status = "Completed"
            };
            _db.OrderHandovers.Add(handover);
            await _failureInjector.ExecuteAsync("AfterHandoverAdded", ct);

            var pickupScan = new PickupScan
            {
                TripId = validation.TripId,
                OrderId = linkedDailyOrder?.Id ?? stop.DailyOrderId,
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
            await _failureInjector.ExecuteAsync("AfterScanAdded", ct);

            if (stop.MealBarcodeId.HasValue)
            {
                var barcode = await _db.MealBarcodes.FirstOrDefaultAsync(b => b.Id == stop.MealBarcodeId.Value, ct);
                if (barcode != null)
                {
                    barcode.Status = MealBarcodeStatus.Scanned;
                    barcode.ScannedAtUtc = nowUtc;
                }
            }

            await _failureInjector.ExecuteAsync("AfterAuditAdded", ct);

            _db.OutboxMessages.Add(new OutboxMessage
            {
                EventType = "OrderHandoverCompleted",
                AggregateType = "OrderHandover",
                AggregateId = handover.Id.ToString(),
                CorrelationId = validation.TripId.ToString(),
                DeduplicationKey = $"order-handover-{handover.Id}",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    HandoverId = handover.Id,
                    BoxId = request.BoxId,
                    DriverId = driver.Id,
                    TripId = validation.TripId,
                    DailyOrderId = handover.DailyOrderId,
                    HandedOverAtUtc = nowUtc
                }),
                CreatedAtUtc = nowUtc
            });
            await _failureInjector.ExecuteAsync("AfterOutboxAdded", ct);

            // Derive authoritative counts
            var tripStops = await _db.TripStops
                .Where(s => s.TripId == validation.TripId)
                .ToListAsync(ct);

            var assignedBoxesCount = tripStops.Count;
            var receivedBoxesCount = tripStops.Count(s => s.Status == TripStopStatus.PickedUp || s.Status == TripStopStatus.Delivered);
            var remainingBoxesCount = Math.Max(0, assignedBoxesCount - receivedBoxesCount);
            var allBoxesReceived = remainingBoxesCount == 0 && assignedBoxesCount > 0;

            var validatedBoxesCount = await _db.PickupValidations
                .Where(v => v.TripId == validation.TripId && v.DriverAssignmentId == validation.DriverAssignmentId)
                .Select(v => v.BoxId)
                .Distinct()
                .CountAsync(ct);

            var nextAction = allBoxesReceived ? "ShowPickupSummary" : "ShowBoxSuccess";

            response = new ConfirmPickupBoxResponseDto(
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

            await _failureInjector.ExecuteAsync("BeforeCommit", ct);

            await _db.SaveChangesAsync(ct);
        }

        if (_transactionExecutor != null)
        {
            await _transactionExecutor.ExecuteAsync(ExecutePickupMutationsAsync, cancellationToken);
        }
        else
        {
            await ExecutePickupMutationsAsync(cancellationToken);
        }

        // Consolidated restaurant alert: one notification when the driver has
        // scanned and photographed every box of the trip (never per box).
        if (_restaurantNotifier is not null && response!.AllBoxesReceived)
        {
            var trip = await _db.Trips.AsNoTracking()
                .Where(t => t.Id == validation.TripId)
                .Select(t => new { t.Code, t.RestaurantId })
                .FirstOrDefaultAsync(cancellationToken);

            await _restaurantNotifier.NotifyPickupCompletedAsync(
                trip?.RestaurantId ?? Guid.Empty,
                validation.TripId,
                trip?.Code ?? string.Empty,
                driver.FullNameAr,
                driver.FullNameEn,
                response.AssignedBoxesCount,
                cancellationToken);
        }

        return Result.Success(response!);
    }

    private static string ComputeRequestHash(ConfirmPickupBoxCommand request)
    {
        var raw = $"{request.BoxId}:{request.ValidationToken}:{request.ConditionPhotoStorageKey}:{request.Latitude}:{request.Longitude}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}
