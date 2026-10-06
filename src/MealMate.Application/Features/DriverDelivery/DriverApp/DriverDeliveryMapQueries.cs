using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record GetDriverDeliveryMapQuery(
    Guid? DriverId = null,
    Guid? FocusedStopId = null) : IQuery<DriverDeliveryMapResponse>;

public sealed class DriverDeliveryMapQueryHandler
    : IQueryHandler<GetDriverDeliveryMapQuery, DriverDeliveryMapResponse>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IDriverDeliveryNavigationBuilder _navigation;

    public DriverDeliveryMapQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages,
        IDriverDeliveryNavigationBuilder navigation)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
        _navigation = navigation;
    }

    public async Task<Result<DriverDeliveryMapResponse>> Handle(
        GetDriverDeliveryMapQuery request,
        CancellationToken cancellationToken)
    {
        var driver = await ResolveDriverAsync(request.DriverId, cancellationToken);
        if (driver is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, "لم يتم العثور على حساب السائق.");
        }

        var isAr = _messages.IsArabic();

        // 1. Find all active trips assigned to driver (dispatcher may add stops to
        //    existing trips or open new ones — the map must cover them all)
        var trips = await DriverResolutionHelper.GetActiveTripsAsync(_db, driver.Id, cancellationToken);

        if (trips.Count == 0)
        {
            return Error.NotFound("DriverTrip.NotFound", isAr ? "لا توجد رحلة نشطة حالياً لهذا السائق." : "No active delivery trip found for this driver.");
        }

        var tripIds = trips.Select(t => t.Id).ToList();

        var dbStops = await _db.TripStops
            .Where(s => tripIds.Contains(s.TripId))
            .ToListAsync(cancellationToken);

        dbStops = DriverResolutionHelper.OrderStopsAcrossTrips(trips, dbStops);

        if (dbStops.Count == 0)
        {
            return Error.NotFound("DriverTrip.NotFound", isAr ? "لا توجد رحلة نشطة حالياً لهذا السائق." : "No active delivery trip found for this driver.");
        }

        var totalCount = dbStops.Count;
        var completedCount = dbStops.Count(s => s.Status == TripStopStatus.Delivered);

        // Customer notes come from the delivery address chosen on each stop's
        // subscription day. Two batched lookups for the whole list — never a
        // per-card query, and never a routing-provider call per card.
        var subscriptionDayIds = dbStops
            .Where(s => s.SubscriptionDayId.HasValue)
            .Select(s => s.SubscriptionDayId!.Value)
            .Distinct()
            .ToList();

        var addressIdsByDay = await _db.SubscriptionDays
            .AsNoTracking()
            .Where(d => subscriptionDayIds.Contains(d.Id) && d.DeliveryAddressId.HasValue)
            .Select(d => new { d.Id, d.DeliveryAddressId })
            .ToDictionaryAsync(x => x.Id, x => x.DeliveryAddressId!.Value, cancellationToken);

        var addressIds = addressIdsByDay.Values.Distinct().ToList();
        var noteByAddressId = await _db.CustomerAddresses
            .AsNoTracking()
            .Where(a => addressIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Notes })
            .ToDictionaryAsync(x => x.Id, x => x.Notes, cancellationToken);

        // 2. Identify the focused stop. An explicit focus must belong to one of the
        //    driver's own active trips — silently showing a different stop would
        //    leak another driver's / an unknown stop into this driver's map.
        TripStop? activeStop = null;
        if (request.FocusedStopId.HasValue)
        {
            activeStop = dbStops.FirstOrDefault(s => s.Id == request.FocusedStopId.Value);
            if (activeStop is null)
            {
                return Error.NotFound("DriverMap.StopNotFound",
                    isAr ? "المحطة المطلوبة غير موجودة ضمن توصيلاتك النشطة." : "The requested stop is not part of your active deliveries.");
            }
        }

        activeStop ??= dbStops.FirstOrDefault(s => s.Status == TripStopStatus.PickedUp)
                      ?? dbStops.FirstOrDefault(s => s.Status == TripStopStatus.Pending)
                      ?? dbStops.First();

        // 3. The response trip always matches the focused stop's trip, even when
        //    the driver picked a stop from an older trip; the card list itself
        //    stays aggregated across all active trips.
        var responseTrip = trips.FirstOrDefault(t => t.Id == activeStop.TripId) ?? trips[0];

        var stopDtos = new List<DriverMapStopDto>();
        for (var i = 0; i < dbStops.Count; i++)
        {
            var s = dbStops[i];
            var isCurrent = s.Id == activeStop.Id;
            var seqBadge = $"{i + 1}/{totalCount}";

            string statusText;
            string statusColor;
            string statusKey;

            switch (s.Status)
            {
                case TripStopStatus.Delivered:
                    statusKey = "Delivered";
                    statusText = isAr ? "تم التسليم" : "Delivered";
                    statusColor = "gray";
                    break;
                case TripStopStatus.PickedUp:
                    statusKey = "InTransit";
                    statusText = isAr ? "خارج التوصيل" : "Out for delivery";
                    statusColor = "green";
                    break;
                default:
                    statusKey = "Pending";
                    statusText = isAr ? "في الطريق للعميل" : "On the way";
                    statusColor = "orange";
                    break;
            }

            var mealSummary = isAr ? s.MealSummaryAr : s.MealSummaryEn;
            if (string.IsNullOrWhiteSpace(mealSummary))
            {
                mealSummary = isAr ? $"{s.BoxCount} وجبات" : $"{s.BoxCount} meals";
            }

            var addrShort = isAr
                ? (s.ZoneNameAr ?? string.Empty)
                : (s.ZoneNameEn ?? string.Empty);

            var fullAddr = !string.IsNullOrWhiteSpace(s.FullAddress)
                ? s.FullAddress
                : isAr
                    ? (s.ZoneNameAr ?? string.Empty)
                    : (s.ZoneNameEn ?? string.Empty);

            string? customerNote = null;
            if (s.SubscriptionDayId.HasValue
                && addressIdsByDay.TryGetValue(s.SubscriptionDayId.Value, out var deliveryAddressId))
            {
                noteByAddressId.TryGetValue(deliveryAddressId, out customerNote);
            }

            stopDtos.Add(new DriverMapStopDto(
                StopId: s.Id,
                BoxId: s.Id,
                TripId: s.TripId,
                SequenceBadge: seqBadge,
                SequenceNumber: s.SequenceNumber > 0 ? s.SequenceNumber : i + 1,
                BoxCode: s.OrderCode ?? $"BX-{s.Id.ToString()[..8]}",
                CustomerName: s.CustomerName ?? string.Empty,
                // The customer's phone is never sent to the driver's map; the
                // driver app builds contact actions from the call-proxy API only.
                CustomerPhone: string.Empty,
                AddressShort: addrShort,
                FullAddress: fullAddr,
                MealsCount: s.BoxCount > 0 ? s.BoxCount : 1,
                MealsSummary: mealSummary,
                DeliveryTimeSlot: s.SlotLabel ?? string.Empty,
                Status: statusKey,
                StatusText: statusText,
                StatusColor: statusColor,
                IsCurrent: isCurrent,
                Latitude: (double?)s.Latitude,
                Longitude: (double?)s.Longitude,
                CustomerNote: customerNote,
                ArrivedAtUtc: s.ArrivedAtUtc
            ));
        }

        var focusedDto = stopDtos.FirstOrDefault(s => s.IsCurrent) ?? stopDtos.First();

        var routeWaypoints = stopDtos
            .Where(s => s.Latitude.HasValue && s.Longitude.HasValue)
            .Select(s => new DriverMapWaypointDto(s.Latitude!.Value, s.Longitude!.Value))
            .ToList();

        // 4. Live driver position: latest tracking fix first, the work-session
        //    start only when no fix exists (the navigation builder classifies the
        //    shift start as never-live regardless).
        var latestTracking = await _db.DeliveryTrackingPoints
            .Where(t => t.DriverProfileId == driver.Id)
            .OrderByDescending(t => t.RecordedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        DriverWorkSession? activeSession = null;
        if (latestTracking is null)
        {
            activeSession = await _db.DriverWorkSessions
                .Where(s => s.DriverProfileId == driver.Id && s.Status == "Active")
                .FirstOrDefaultAsync(cancellationToken);
        }

        var driverLat = (double?)latestTracking?.Latitude;
        var driverLon = (double?)latestTracking?.Longitude;

        if (!driverLat.HasValue || !driverLon.HasValue)
        {
            driverLat ??= activeSession?.StartLatitude;
            driverLon ??= activeSession?.StartLongitude;
        }

        var navigation = await _navigation.BuildAsync(
            driver.Id, focusedDto, latestTracking, activeSession, isAr, cancellationToken);

        return Result.Success(new DriverDeliveryMapResponse(
            TripId: responseTrip.Id,
            TripCode: responseTrip.Code ?? string.Empty,
            DriverLatitude: driverLat,
            DriverLongitude: driverLon,
            TotalStopsCount: totalCount,
            CompletedStopsCount: completedCount,
            FocusedStop: focusedDto,
            Stops: stopDtos,
            RoutePolylineWaypoints: routeWaypoints,
            Navigation: navigation
        ));
    }

    private Task<DriverProfile?> ResolveDriverAsync(Guid? driverId, CancellationToken cancellationToken)
        => DriverResolutionHelper.ResolveDriverAsync(_db, _currentUser, driverId, cancellationToken);
}
