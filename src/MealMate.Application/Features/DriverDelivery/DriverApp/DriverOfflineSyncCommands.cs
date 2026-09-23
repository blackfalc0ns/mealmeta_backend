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

    public DriverOfflineSyncCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<DriverOfflineSyncResponseDto>> Handle(
        SyncDriverOfflineQueueCommand request,
        CancellationToken cancellationToken)
    {
        var driverId = request.DriverId;
        if (!driverId.HasValue && _currentUser.UserId.HasValue)
        {
            driverId = await _db.DriverProfiles
                .Where(d => d.UserId == _currentUser.UserId.Value)
                .Select(d => d.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

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

                var eventTime = item.CapturedAtUtc ?? now;

                switch (item.ActionType.ToLowerInvariant())
                {
                    case "pickup":
                    case "pickupscan":
                        if (stop.Status != TripStopStatus.PickedUp && stop.Status != TripStopStatus.Delivered)
                        {
                            stop.Status = TripStopStatus.PickedUp;
                            _db.PickupScans.Add(new PickupScan
                            {
                                OrderId = stop.DailyOrderId,
                                SubscriptionDayId = stop.SubscriptionDayId,
                                DriverProfileId = driverId ?? Guid.Empty,
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
                        if (stop.Status != TripStopStatus.Delivered)
                        {
                            stop.Status = TripStopStatus.Delivered;
                            stop.DeliveredAtUtc = eventTime;

                            _db.DeliveryConfirmationScans.Add(new DeliveryConfirmationScan
                            {
                                OrderId = stop.DailyOrderId,
                                SubscriptionDayId = stop.SubscriptionDayId,
                                DriverProfileId = driverId ?? Guid.Empty,
                                BarcodeValue = item.BarcodeValue ?? stop.OrderCode ?? "DELIVERED",
                                PhotoPath = item.PhotoStorageKey ?? "offline_delivery_photo.jpg",
                                Latitude = item.Latitude,
                                Longitude = item.Longitude,
                                ScannedAtUtc = eventTime
                            });

                            if (driverId.HasValue)
                            {
                                var session = await _db.DriverWorkSessions
                                    .FirstOrDefaultAsync(s => s.DriverProfileId == driverId.Value && s.Status == "Active", cancellationToken);
                                if (session is not null) session.CompletedOrdersCount += 1;
                            }
                        }
                        results.Add(new OfflineSyncItemResultDto(item.ClientActionId, item.BoxId, item.ActionType, IsSuccess: true, ErrorMessage: null));
                        break;

                    case "fail":
                    case "failed":
                    case "faileddelivery":
                    case "faildelivery":
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
