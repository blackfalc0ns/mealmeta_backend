using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record GetDriverPickupPointQuery(
    Guid? DriverId = null) : IQuery<DriverPickupPointResponse>;

public sealed record GetDriverPickupManifestQuery(
    Guid? DriverId = null,
    string? StatusFilter = null) : IQuery<DriverPickupManifestResponse>;

public sealed class DriverPickupQueryHandlers
    : IQueryHandler<GetDriverPickupPointQuery, DriverPickupPointResponse>,
      IQueryHandler<GetDriverPickupManifestQuery, DriverPickupManifestResponse>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public DriverPickupQueryHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<DriverPickupPointResponse>> Handle(
        GetDriverPickupPointQuery request,
        CancellationToken cancellationToken)
    {
        var driver = await ResolveDriverAsync(request.DriverId, cancellationToken);
        if (driver is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, "لم يتم العثور على حساب السائق.");
        }

        var isAr = _messages.IsArabic();

        var trip = await (
            from da in _db.DriverAssignments
            join t in _db.Trips on da.TripId equals t.Id
            where da.DriverProfileId == driver.Id && da.Status == "Active"
            orderby t.CreatedAtUtc descending
            select t
        ).FirstOrDefaultAsync(cancellationToken);

        if (trip is null)
        {
            return Error.NotFound("Trips.NoActiveTrip", isAr ? "لا توجد رحلة نشطة حالياً لهذا السائق." : "No active trip found for this driver.");
        }

        MealMate.Domain.IdentityAccess.RestaurantProfile? restaurant = null;
        if (trip.RestaurantId != Guid.Empty)
        {
            restaurant = await _db.RestaurantProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == trip.RestaurantId, cancellationToken);
        }

        var stopsCount = await _db.TripStops.CountAsync(s => s.TripId == trip.Id, cancellationToken);
        var hasArrived = trip.Notes?.Contains("[DriverArrivedAtKitchen]") ?? false;

        var kitchenName = isAr
            ? (restaurant?.TradeNameAr ?? restaurant?.LegalNameAr ?? "المطبخ الرئيسي")
            : (restaurant?.TradeNameEn ?? restaurant?.LegalNameEn ?? "Main Kitchen");

        var kitchenAddress = isAr
            ? (restaurant?.TradeNameAr ?? string.Empty)
            : (restaurant?.TradeNameEn ?? string.Empty);

        var activeSession = await _db.DriverWorkSessions
            .FirstOrDefaultAsync(s => s.DriverProfileId == driver.Id && s.Status == "Active", cancellationToken);

        string routeDistanceText = string.Empty;
        string estimatedArrivalMinutesText = string.Empty;

        if (activeSession?.StartLatitude != null && activeSession?.StartLongitude != null &&
            restaurant?.Latitude != null && restaurant?.Longitude != null)
        {
            var dLat = ((double)restaurant.Latitude.Value - (double)activeSession.StartLatitude.Value) * Math.PI / 180.0;
            var dLon = ((double)restaurant.Longitude.Value - (double)activeSession.StartLongitude.Value) * Math.PI / 180.0;
            var lat1 = (double)activeSession.StartLatitude.Value * Math.PI / 180.0;
            var lat2 = (double)restaurant.Latitude.Value * Math.PI / 180.0;

            var a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) +
                    Math.Cos(lat1) * Math.Cos(lat2) *
                    Math.Sin(dLon / 2.0) * Math.Sin(dLon / 2.0);
            var c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
            var distKm = 6371.0 * c;
            var etaMinutes = Math.Max(1, (int)Math.Ceiling(distKm * 2.0 + 2.0));

            routeDistanceText = isAr ? $"{distKm:0.0} كم" : $"{distKm:0.0} km";
            estimatedArrivalMinutesText = isAr ? $"{etaMinutes} دقيقة" : $"{etaMinutes} mins";
        }

        return Result.Success(new DriverPickupPointResponse(
            TripId: trip.Id,
            TripCode: trip.Code ?? string.Empty,
            KitchenId: trip.RestaurantId,
            KitchenName: kitchenName,
            KitchenAddress: kitchenAddress,
            KitchenLatitude: (double?)restaurant?.Latitude,
            KitchenLongitude: (double?)restaurant?.Longitude,
            TotalBoxesCount: stopsCount,
            RouteDistanceText: routeDistanceText,
            EstimatedArrivalMinutesText: estimatedArrivalMinutesText,
            HasArrivedAtKitchen: hasArrived,
            ArrivedAtKitchenAtUtc: hasArrived ? trip.StartedAtUtc : null
        ));
    }

    public async Task<Result<DriverPickupManifestResponse>> Handle(
        GetDriverPickupManifestQuery request,
        CancellationToken cancellationToken)
    {
        var driver = await ResolveDriverAsync(request.DriverId, cancellationToken);
        if (driver is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, "لم يتم العثور على حساب السائق.");
        }

        var isAr = _messages.IsArabic();

        var trip = await (
            from da in _db.DriverAssignments
            join t in _db.Trips on da.TripId equals t.Id
            where da.DriverProfileId == driver.Id && da.Status == "Active"
            orderby t.CreatedAtUtc descending
            select t
        ).FirstOrDefaultAsync(cancellationToken);

        if (trip is null)
        {
            return Result.Success(new DriverPickupManifestResponse(
                TripId: Guid.Empty,
                TripCode: string.Empty,
                KitchenName: string.Empty,
                TotalBoxesCount: 0,
                ScannedBoxesCount: 0,
                RemainingBoxesCount: 0,
                AllBoxesScanned: false,
                Boxes: Array.Empty<DriverPickupBoxItemDto>()
            ));
        }

        var boxes = new List<DriverPickupBoxItemDto>();

        MealMate.Domain.IdentityAccess.RestaurantProfile? restaurantProfile = null;
        if (trip.RestaurantId != Guid.Empty)
        {
            restaurantProfile = await _db.RestaurantProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == trip.RestaurantId, cancellationToken);
        }

        var kitchenName = isAr
            ? (restaurantProfile?.TradeNameAr ?? restaurantProfile?.LegalNameAr ?? "المطبخ الرئيسي")
            : (restaurantProfile?.TradeNameEn ?? restaurantProfile?.LegalNameEn ?? "Main Kitchen");

        var stops = await _db.TripStops
            .Where(s => s.TripId == trip.Id)
            .OrderBy(s => s.SequenceNumber)
            .ToListAsync(cancellationToken);

        var scansList = await _db.PickupScans
            .Where(p => p.TripId == trip.Id)
            .OrderByDescending(p => p.ScannedAtUtc)
            .ToListAsync(cancellationToken);

        var scansByOrder = scansList
            .Where(p => p.OrderId.HasValue)
            .GroupBy(p => p.OrderId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var scansByDay = scansList
            .Where(p => p.SubscriptionDayId.HasValue)
            .GroupBy(p => p.SubscriptionDayId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        for (var i = 0; i < stops.Count; i++)
        {
            var s = stops[i];
            var isScanned = s.Status == TripStopStatus.PickedUp || s.Status == TripStopStatus.Delivered;
            var scanKey = isScanned ? "PickedUp" : "PendingScan";
            var scanText = isScanned
                ? (isAr ? "تم الاستلام" : "Picked Up")
                : (isAr ? "لم يتم المسح" : "Pending Scan");

            var mealText = isAr ? s.MealSummaryAr : s.MealSummaryEn;
            if (string.IsNullOrWhiteSpace(mealText)) mealText = isAr ? $"{s.BoxCount} وجبات" : $"{s.BoxCount} meals";

            var zone = isAr ? s.ZoneNameAr : s.ZoneNameEn;

            PickupScan? scan = null;
            if (s.DailyOrderId.HasValue && scansByOrder.TryGetValue(s.DailyOrderId.Value, out var orderScan))
            {
                scan = orderScan;
            }
            else if (s.SubscriptionDayId.HasValue && scansByDay.TryGetValue(s.SubscriptionDayId.Value, out var dayScan))
            {
                scan = dayScan;
            }

            boxes.Add(new DriverPickupBoxItemDto(
                BoxId: s.Id,
                BoxCode: s.OrderCode ?? $"BX-{s.Id.ToString()[..8]}",
                CustomerName: s.CustomerName ?? string.Empty,
                DeliveryZone: zone ?? string.Empty,
                MealsCount: s.BoxCount > 0 ? s.BoxCount : 1,
                MealsSummary: mealText,
                DeliveryTimeSlot: s.SlotLabel ?? string.Empty,
                ScanStatus: scanKey,
                ScanStatusText: scanText,
                IsScanned: isScanned,
                ScannedAtUtc: scan?.ScannedAtUtc,
                ConditionPhotoStorageKey: scan?.PhotoPath
            ));
        }

        // Apply filter if specified
        var filteredBoxes = boxes;
        if (!string.IsNullOrWhiteSpace(request.StatusFilter))
        {
            if (request.StatusFilter.Equals("PendingScan", StringComparison.OrdinalIgnoreCase))
            {
                filteredBoxes = boxes.Where(b => !b.IsScanned).ToList();
            }
            else if (request.StatusFilter.Equals("PickedUp", StringComparison.OrdinalIgnoreCase) ||
                     request.StatusFilter.Equals("Scanned", StringComparison.OrdinalIgnoreCase))
            {
                filteredBoxes = boxes.Where(b => b.IsScanned).ToList();
            }
        }

        var total = boxes.Count;
        var scanned = boxes.Count(b => b.IsScanned);
        var remaining = total - scanned;
        var allScanned = remaining == 0 && total > 0;

        return Result.Success(new DriverPickupManifestResponse(
            TripId: trip.Id,
            TripCode: trip.Code ?? string.Empty,
            KitchenName: kitchenName,
            TotalBoxesCount: total,
            ScannedBoxesCount: scanned,
            RemainingBoxesCount: remaining,
            AllBoxesScanned: allScanned,
            Boxes: filteredBoxes
        ));
    }

    private async Task<DriverProfile?> ResolveDriverAsync(Guid? driverId, CancellationToken cancellationToken)
    {
        if (driverId.HasValue && driverId.Value != Guid.Empty)
        {
            return await _db.DriverProfiles.FirstOrDefaultAsync(d => d.Id == driverId.Value, cancellationToken);
        }

        if (_currentUser.UserId.HasValue)
        {
            var driver = await _db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == _currentUser.UserId.Value, cancellationToken);
            if (driver is not null) return driver;
        }

        return null;
    }
}
