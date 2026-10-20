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

    public DriverOfflineSyncCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages,
        IApplicationTransactionExecutor? transactions = null,
        IApplicationLockManager? locks = null)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
        _transactions = transactions;
        _locks = locks;
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
                        if (stop.Status == TripStopStatus.ReturnedToKitchen || stop.Status == TripStopStatus.Failed)
                        {
                            results.Add(new OfflineSyncItemResultDto(item.ClientActionId, item.BoxId, item.ActionType, false, "Stop cannot be reopened."));
                            break;
                        }
                        if (stop.Status != TripStopStatus.PickedUp && stop.Status != TripStopStatus.Delivered)
                        {
                            stop.Status = TripStopStatus.PickedUp;
                            _db.PickupScans.Add(new PickupScan
                            {
                                OrderId = stop.DailyOrderId,
                                SubscriptionDayId = stop.SubscriptionDayId,
                                DriverProfileId = driverId,
                                BarcodeValue = item.BarcodeValue ?? stop.OrderCode ?? "PICKED_UP",
                                PhotoPath = item.PhotoStorageKey ?? "offline_sync_photo.jpg",
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
                                BarcodeValue = item.BarcodeValue ?? stop.OrderCode ?? "DELIVERED",
                                PhotoPath = item.PhotoStorageKey ?? "offline_delivery_photo.jpg",
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
}
