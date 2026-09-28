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

    public DriverDeliveryMapQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
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

        // 1. Find active trip assigned to driver
        var trip = await (
            from da in _db.DriverAssignments
            join t in _db.Trips on da.TripId equals t.Id
            where da.DriverProfileId == driver.Id && da.Status == "Active"
            orderby t.CreatedAtUtc descending
            select t
        ).FirstOrDefaultAsync(cancellationToken);

        if (trip is not null)
        {
            var dbStops = await _db.TripStops
                .Where(s => s.TripId == trip.Id)
                .OrderBy(s => s.SequenceNumber)
                .ToListAsync(cancellationToken);

            if (dbStops.Count > 0)
            {
                var totalCount = dbStops.Count;
                var completedCount = dbStops.Count(s => s.Status == TripStopStatus.Delivered);

                // Identify active/focused stop
                TripStop? activeStop = null;
                if (request.FocusedStopId.HasValue)
                {
                    activeStop = dbStops.FirstOrDefault(s => s.Id == request.FocusedStopId.Value);
                }

                activeStop ??= dbStops.FirstOrDefault(s => s.Status == TripStopStatus.PickedUp)
                              ?? dbStops.FirstOrDefault(s => s.Status == TripStopStatus.Pending)
                              ?? dbStops.First();

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

                    var fullAddr = isAr
                        ? (s.ZoneNameAr ?? string.Empty)
                        : (s.ZoneNameEn ?? string.Empty);

                    stopDtos.Add(new DriverMapStopDto(
                        StopId: s.Id,
                        SequenceBadge: seqBadge,
                        SequenceNumber: s.SequenceNumber > 0 ? s.SequenceNumber : i + 1,
                        BoxCode: s.OrderCode ?? $"BX-{s.Id.ToString()[..8]}",
                        CustomerName: s.CustomerName ?? string.Empty,
                        CustomerPhone: s.CustomerPhone ?? string.Empty,
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
                        Longitude: (double?)s.Longitude
                    ));
                }

                var focusedDto = stopDtos.FirstOrDefault(s => s.IsCurrent) ?? stopDtos.First();

                var routeWaypoints = stopDtos
                    .Where(s => s.Latitude.HasValue && s.Longitude.HasValue)
                    .Select(s => new DriverMapWaypointDto(s.Latitude!.Value, s.Longitude!.Value))
                    .ToList();

                var latestTracking = await _db.DeliveryTrackingPoints
                    .Where(t => t.DriverProfileId == driver.Id)
                    .OrderByDescending(t => t.RecordedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                var driverLat = (double?)latestTracking?.Latitude;
                var driverLon = (double?)latestTracking?.Longitude;

                if (!driverLat.HasValue || !driverLon.HasValue)
                {
                    var activeSession = await _db.DriverWorkSessions
                        .Where(s => s.DriverProfileId == driver.Id && s.Status == "Active")
                        .FirstOrDefaultAsync(cancellationToken);
                    driverLat ??= activeSession?.StartLatitude;
                    driverLon ??= activeSession?.StartLongitude;
                }

                return Result.Success(new DriverDeliveryMapResponse(
                    TripId: trip.Id,
                    TripCode: trip.Code ?? string.Empty,
                    DriverLatitude: driverLat,
                    DriverLongitude: driverLon,
                    TotalStopsCount: totalCount,
                    CompletedStopsCount: completedCount,
                    FocusedStop: focusedDto,
                    Stops: stopDtos,
                    RoutePolylineWaypoints: routeWaypoints
                ));
            }
        }

        return Error.NotFound("DriverTrip.NotFound", isAr ? "لا توجد رحلة نشطة حالياً لهذا السائق." : "No active delivery trip found for this driver.");
    }

    private Task<DriverProfile?> ResolveDriverAsync(Guid? driverId, CancellationToken cancellationToken)
        => DriverResolutionHelper.ResolveDriverAsync(_db, _currentUser, driverId, cancellationToken);
}
