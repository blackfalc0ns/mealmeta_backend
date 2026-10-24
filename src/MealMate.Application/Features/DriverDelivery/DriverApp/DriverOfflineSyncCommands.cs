using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record SyncDriverOfflineQueueCommand(
    IReadOnlyList<OfflineQueueItemDto> Items,
    Guid? DriverId = null) : ICommand<DriverOfflineSyncResponseDto>;

public sealed record OfflineQueueItemDto(
    Guid ClientActionId,
    string ActionType,
    Guid BoxId,
    string? BarcodeValue = null,
    string? PhotoStorageKey = null,
    string? DeliveryOtp = null,
    string? ReasonCategory = null,
    string? Notes = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    DateTime? CapturedAtUtc = null);

public sealed record DriverOfflineSyncResponseDto(
    int TotalReceived,
    int SuccessCount,
    int FailedCount,
    IReadOnlyList<OfflineSyncItemResultDto> Results,
    DateTime SyncedAtUtc);

public sealed record OfflineSyncItemResultDto(
    Guid ClientActionId,
    Guid BoxId,
    string ActionType,
    bool IsSuccess,
    string? ErrorMessage);

public sealed class DriverOfflineSyncCommandHandler
    : ICommandHandler<SyncDriverOfflineQueueCommand, DriverOfflineSyncResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IApplicationTransactionExecutor? _transactions;
    private readonly IApplicationLockManager? _locks;
    private readonly MealMate.Application.Common.Storage.SupportEvidenceScanService? _evidenceScans;

    public DriverOfflineSyncCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages,
        IApplicationTransactionExecutor? transactions = null,
        IApplicationLockManager? locks = null,
        MealMate.Application.Common.Storage.SupportEvidenceScanService? evidenceScans = null)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
        _transactions = transactions;
        _locks = locks;
        _evidenceScans = evidenceScans;
    }

    public async Task<Result<DriverOfflineSyncResponseDto>> Handle(
        SyncDriverOfflineQueueCommand request,
        CancellationToken cancellationToken)
    {
        if (_transactions is null || _locks is null)
            return Error.Conflict("TRIP_TRANSACTION_UNAVAILABLE", _messages.GetMessage("driver_support_handover.transaction_unavailable", "Offline sync requires transactional locks."));
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
            return Error.Unauthorized("auth.invalid_credentials", _messages.GetMessage("auth.invalid_credentials", "Authentication is required."));
        var driverId = await _db.DriverProfiles.Where(d => d.UserId == _currentUser.UserId.Value && !d.IsDeleted)
            .Select(d => (Guid?)d.Id).FirstOrDefaultAsync(cancellationToken);
        if (!driverId.HasValue) return Error.Forbidden("DRIVER_IDENTITY_MISMATCH", _messages.GetMessage("driver.identity_mismatch", "Authenticated user is not a driver."));
        if (request.DriverId.HasValue && request.DriverId != driverId)
            return Error.Forbidden("DRIVER_IDENTITY_MISMATCH", _messages.GetMessage("driver.identity_mismatch", "The requested driver does not match the authenticated driver."));
        return await _transactions.ExecuteResultAsync(ct => SyncCoreAsync(request, driverId.Value, ct), cancellationToken);
    }

    private async Task<Result<DriverOfflineSyncResponseDto>> SyncCoreAsync(
        SyncDriverOfflineQueueCommand request, Guid driverId, CancellationToken cancellationToken)
    {
        var stopIds = request.Items.Select(x => x.BoxId).Distinct().ToArray();
        var tripIds = await _db.TripStops.AsNoTracking().Where(s => stopIds.Contains(s.Id))
            .Select(s => s.TripId).Distinct().OrderBy(id => id).ToListAsync(cancellationToken);
        foreach (var tripId in tripIds) await _locks!.LockTripAsync(tripId, cancellationToken);
        await _locks!.LockDriverAsync(driverId, cancellationToken);
        if (!(await _db.GetActiveAccountUserIdsAsync(new[] { _currentUser.UserId!.Value }, cancellationToken)).Contains(_currentUser.UserId.Value))
            return Error.Forbidden("DRIVER_ACCOUNT_INACTIVE", _messages.GetMessage("driver.account_inactive", "Driver account is not active."));

        var results = new List<OfflineSyncItemResultDto>();
        var now = DateTime.UtcNow;

        foreach (var item in request.Items)
        {
            try
            {
                var stop = await _db.TripStops
                    .Include(s => s.Trip)
                    .FirstOrDefaultAsync(s => s.Id == item.BoxId, cancellationToken);

                if (stop is null)
                {
                    results.Add(new OfflineSyncItemResultDto(
                        item.ClientActionId,
                        item.BoxId,
                        item.ActionType,
                        IsSuccess: false,
                        ErrorMessage: "Stop not found"));
                    continue;
                }

                if (stop.Trip.Status is TripStatus.Completed or TripStatus.Cancelled)
                {
                    results.Add(new OfflineSyncItemResultDto(item.ClientActionId, item.BoxId, item.ActionType, false, "Terminal trip cannot accept offline mutations."));
                    continue;
                }

                var assignedToDriver = await _db.DriverAssignments.AnyAsync(a => a.TripId == stop.TripId
                    && a.DriverProfileId == driverId && (a.Status == "Active" || a.Status == "Assigned"), cancellationToken);
                if (!assignedToDriver || await DriverSupportHandoverMutationGuard.HasActiveHandoverForTripAsync(_db, stop.TripId, cancellationToken)
                    || await DriverSupportHandoverMutationGuard.HasActiveHandoverForStopAsync(_db, stop.Id, cancellationToken)
                    || await DriverSupportHandoverMutationGuard.HasActiveHoldForStopAsync(_db, stop.Id, cancellationToken))
                {
                    results.Add(new OfflineSyncItemResultDto(item.ClientActionId, item.BoxId, item.ActionType, false,
                        _messages.GetMessage("driver_support_handover.pending", "The stop is not currently available to this driver.")));
                    continue;
                }

                var eventTime = item.CapturedAtUtc ?? now;

                switch (item.ActionType.ToLowerInvariant())
                {
                    case "pickup":
                    case "pickupscan":
                        if (!await ValidateOfflineEvidenceAsync(item.PhotoStorageKey, driverId, cancellationToken))
                        {
                            results.Add(new OfflineSyncItemResultDto(item.ClientActionId, item.BoxId, item.ActionType, false,
                                "A clean owned delivery evidence upload is required."));
                            break;
                        }
                        if (stop.Status == TripStopStatus.ReturnedToKitchen || stop.Status == TripStopStatus.Failed)
                        {
                            results.Add(new OfflineSyncItemResultDto(item.ClientActionId, item.BoxId, item.ActionType, false, "Stop cannot be reopened."));
                            break;
                        }
                        if (stop.Status != TripStopStatus.PickedUp && stop.Status != TripStopStatus.Delivered)
                        {
                            var pickupCode = string.IsNullOrWhiteSpace(item.BarcodeValue)
                                ? string.Empty : DeliveryPickupQrGuard.Normalize(item.BarcodeValue);
                            var matchesLabel = pickupCode.Length > 0
                                && (!stop.MealBarcodeId.HasValue && !string.IsNullOrWhiteSpace(stop.OrderCode)
                                    && pickupCode == DeliveryPickupQrGuard.Normalize(stop.OrderCode)
                                    || await _db.MealBarcodes.AnyAsync(b =>
                                        !b.IsRevoked && b.Status != MealBarcodeStatus.Revoked && b.Status != MealBarcodeStatus.Archived
                                        && (stop.MealBarcodeId.HasValue ? b.Id == stop.MealBarcodeId
                                            : stop.SubscriptionDayId.HasValue && b.SubscriptionDayId == stop.SubscriptionDayId
                                                && (!b.TripStopId.HasValue || b.TripStopId == stop.Id))
                                        && (b.BarcodeValue == pickupCode || b.BarcodeValue == "#" + pickupCode), cancellationToken));
                            if (!matchesLabel)
                            {
                                results.Add(new OfflineSyncItemResultDto(item.ClientActionId, item.BoxId, item.ActionType,
                                    false, _messages.GetMessage(pickupCode.Length == 0
                                        ? "Orders.DeliveryBarcodeRequired" : "Orders.BarcodeMismatch")));
                                break;
                            }
                            stop.Status = TripStopStatus.PickedUp;
                            _db.PickupScans.Add(new PickupScan
                            {
                                TripId = stop.TripId,
                                OrderId = stop.DailyOrderId,
                                SubscriptionDayId = stop.SubscriptionDayId,
                                DriverProfileId = driverId,
                                BarcodeValue = pickupCode,
                                PhotoPath = item.PhotoStorageKey!,
                                Latitude = item.Latitude,
                                Longitude = item.Longitude,
                                ScannedAtUtc = eventTime
                            });
                        }
                        results.Add(new OfflineSyncItemResultDto(item.ClientActionId, item.BoxId, item.ActionType, IsSuccess: true, ErrorMessage: null));
                        break;

                    case "deliver":
                    case "delivery":
                    case "deliveryconfirmation":
                        if (stop.Status != TripStopStatus.Delivered)
                        {
                            var qrError = await DeliveryPickupQrGuard.ValidateAsync(
                                _db, _messages, stop, driverId, item.BarcodeValue, cancellationToken);
                            if (qrError is not null)
                            {
                                results.Add(new OfflineSyncItemResultDto(item.ClientActionId, item.BoxId,
                                    item.ActionType, false, qrError.Description));
                                break;
                            }
                        }
                        if (!await ValidateOfflineEvidenceAsync(item.PhotoStorageKey, driverId, cancellationToken))
                        {
                            results.Add(new OfflineSyncItemResultDto(item.ClientActionId, item.BoxId, item.ActionType, false,
                                "A clean owned delivery evidence upload is required."));
                            break;
                        }
                        if (stop.Status == TripStopStatus.ReturnedToKitchen || stop.Status == TripStopStatus.Failed)
                        {
                            results.Add(new OfflineSyncItemResultDto(item.ClientActionId, item.BoxId, item.ActionType, false, "Stop cannot be reopened."));
                            break;
                        }
                        if (stop.Status != TripStopStatus.Delivered)
                        {
                            stop.Status = TripStopStatus.Delivered;
                            stop.DeliveredAtUtc = eventTime;

                            _db.DeliveryConfirmationScans.Add(new DeliveryConfirmationScan
                            {
                                OrderId = stop.DailyOrderId,
                                SubscriptionDayId = stop.SubscriptionDayId,
                                DriverProfileId = driverId,
                                BarcodeValue = DeliveryPickupQrGuard.Normalize(item.BarcodeValue!),
                                PhotoPath = item.PhotoStorageKey!,
                                Latitude = item.Latitude,
                                Longitude = item.Longitude,
                                ScannedAtUtc = eventTime
                            });

                            var session = await _db.DriverWorkSessions
                                .FirstOrDefaultAsync(s => s.DriverProfileId == driverId && s.Status == "Active", cancellationToken);
                            if (session is not null) session.CompletedOrdersCount += 1;
                        }
                        results.Add(new OfflineSyncItemResultDto(item.ClientActionId, item.BoxId, item.ActionType, IsSuccess: true, ErrorMessage: null));
                        break;

                    case "fail":
                    case "failed":
                    case "faileddelivery":
                    case "faildelivery":
                        if (stop.Status == TripStopStatus.Delivered || stop.Status == TripStopStatus.ReturnedToKitchen)
                        {
                            results.Add(new OfflineSyncItemResultDto(item.ClientActionId, item.BoxId, item.ActionType, false, "Terminal stop cannot be changed."));
                            break;
                        }
                        stop.Status = TripStopStatus.Failed;
                        stop.FailureReason = $"[OfflineSync - {item.ReasonCategory}] {item.Notes}";
                        results.Add(new OfflineSyncItemResultDto(item.ClientActionId, item.BoxId, item.ActionType, IsSuccess: true, ErrorMessage: null));
                        break;

                    default:
                        results.Add(new OfflineSyncItemResultDto(item.ClientActionId, item.BoxId, item.ActionType, IsSuccess: true, ErrorMessage: null));
                        break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                results.Add(new OfflineSyncItemResultDto(
                    item.ClientActionId,
                    item.BoxId,
                    item.ActionType,
                    IsSuccess: false,
                    ErrorMessage: ex.Message));
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        var successCount = results.Count(r => r.IsSuccess);
        var failedCount = results.Count(r => !r.IsSuccess);

        return Result.Success(new DriverOfflineSyncResponseDto(
            TotalReceived: request.Items.Count,
            SuccessCount: successCount,
            FailedCount: failedCount,
            Results: results,
            SyncedAtUtc: now
        ));
    }

    private async Task<bool> ValidateOfflineEvidenceAsync(string? key, Guid driverId, CancellationToken ct)
    {
        if (_evidenceScans is null || string.IsNullOrWhiteSpace(key)
            || !key.StartsWith("uploads/drivers/delivery/", StringComparison.Ordinal)) return false;
        var driverUserId = await _db.DriverProfiles.AsNoTracking().Where(x => x.Id == driverId && !x.IsDeleted)
            .Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct);
        if (driverUserId is null) return false;
        var record = await _db.UploadFileRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.StorageKey == key, ct);
        if (record is null || record.UploadedByUserId != driverUserId || record.Scope != "drivers"
            || record.Folder != "delivery" || record.ContentType is not ("image/jpeg" or "image/png" or "image/webp")) return false;
        var scan = await _evidenceScans.ReadCleanAsync(record, ct);
        return scan.Bytes is not null && MealMate.Application.Features.UnifiedSupport.SupportCaseService.HasValidSignature(scan.Bytes, record.ContentType);
    }
}
