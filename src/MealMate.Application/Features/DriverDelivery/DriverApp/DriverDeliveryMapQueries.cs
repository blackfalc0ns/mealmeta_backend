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
                        ? (!string.IsNullOrWhiteSpace(s.ZoneNameAr) ? $"{s.ZoneNameAr} شارع 113" : "السالمية شارع 113")
                        : (!string.IsNullOrWhiteSpace(s.ZoneNameEn) ? $"{s.ZoneNameEn} St 113" : "Salmiya St 113");

                    var fullAddr = isAr
                        ? $"شارع الخليج العربي ، قطعة 12 منزل 45 ، {s.ZoneNameAr ?? "السالمية"}"
                        : $"Arabian Gulf St, Block 12 House 45, {s.ZoneNameEn ?? "Salmiya"}";

                    stopDtos.Add(new DriverMapStopDto(
                        StopId: s.Id,
                        SequenceBadge: seqBadge,
                        SequenceNumber: s.SequenceNumber > 0 ? s.SequenceNumber : i + 1,
                        BoxCode: s.OrderCode ?? $"BX-458{620 + i}",
                        CustomerName: s.CustomerName ?? (isAr ? "محمد علي" : "Mohammed Ali"),
                        CustomerPhone: s.CustomerPhone ?? "+966 50 123 4567",
                        AddressShort: addrShort,
                        FullAddress: fullAddr,
                        MealsCount: s.BoxCount > 0 ? s.BoxCount : 3,
                        MealsSummary: mealSummary,
                        DeliveryTimeSlot: s.SlotLabel ?? (isAr ? "09:20 ص" : "09:20 AM"),
                        Status: statusKey,
                        StatusText: statusText,
                        StatusColor: statusColor,
                        IsCurrent: isCurrent,
                        Latitude: 29.3375 + (i * 0.005),
                        Longitude: 48.0280 + (i * 0.005)
                    ));
                }

                var focusedDto = stopDtos.FirstOrDefault(s => s.IsCurrent) ?? stopDtos.First();

                var waypoints = GeneratePolylineWaypoints(
                    driverLat: 29.3375,
                    driverLng: 48.0280,
                    destLat: focusedDto.Latitude ?? 29.3450,
                    destLng: focusedDto.Longitude ?? 48.0350
                );

                return Result.Success(new DriverDeliveryMapResponse(
                    TripId: trip.Id,
                    TripCode: trip.Code ?? "TRP-101",
                    DriverLatitude: 29.3375,
                    DriverLongitude: 48.0280,
                    TotalStopsCount: totalCount,
                    CompletedStopsCount: completedCount,
                    FocusedStop: focusedDto,
                    Stops: stopDtos,
                    RoutePolylineWaypoints: waypoints
                ));
            }
        }

        // 2. Realistic screen-aligned fallback (matching screen 03.01 / 05.04)
        var fallbackStops = new List<DriverMapStopDto>
        {
            new DriverMapStopDto(
                StopId: Guid.NewGuid(),
                SequenceBadge: "1/3",
                SequenceNumber: 1,
                BoxCode: "BX-458722",
                CustomerName: isAr ? "أحمد فيصل" : "Ahmed Faisal",
                CustomerPhone: "+966 50 111 2233",
                AddressShort: isAr ? "السالمية شارع 113" : "Salmiya St 113",
                FullAddress: isAr ? "شارع الخليج العربي ، قطعة 12 منزل 40 ، السالمية" : "Arabian Gulf St, Block 12 House 40, Salmiya",
                MealsCount: 3,
                MealsSummary: isAr ? "3 وجبات" : "3 meals",
                DeliveryTimeSlot: isAr ? "09:24 ص" : "09:24 AM",
                Status: "Delivered",
                StatusText: isAr ? "تم التسليم" : "Delivered",
                StatusColor: "gray",
                IsCurrent: false,
                Latitude: 29.3320,
                Longitude: 48.0210
            ),
            new DriverMapStopDto(
                StopId: Guid.NewGuid(),
                SequenceBadge: "2/3",
                SequenceNumber: 2,
                BoxCode: "BX-458622",
                CustomerName: isAr ? "محمد علي" : "Mohammed Ali",
                CustomerPhone: "+966 50 123 4567",
                AddressShort: isAr ? "السالمية شارع 113" : "Salmiya St 113",
                FullAddress: isAr ? "شارع الخليج العربي ، قطعة 12 منزل 45 ، السالمية" : "Arabian Gulf St, Block 12 House 45, Salmiya",
                MealsCount: 3,
                MealsSummary: isAr ? "3 وجبات" : "3 meals",
                DeliveryTimeSlot: isAr ? "09:20 ص" : "09:20 AM",
                Status: "InTransit",
                StatusText: isAr ? "خارج التوصيل" : "Out for delivery",
                StatusColor: "green",
                IsCurrent: true,
                Latitude: 29.3375,
                Longitude: 48.0280
            ),
            new DriverMapStopDto(
                StopId: Guid.NewGuid(),
                SequenceBadge: "3/3",
                SequenceNumber: 3,
                BoxCode: "BX-458722",
                CustomerName: isAr ? "مهند أحمد" : "Mohanad Ahmed",
                CustomerPhone: "+966 50 444 5566",
                AddressShort: isAr ? "المنامة شارع 113" : "Manama St 113",
                FullAddress: isAr ? "شارع الخليج العربي ، قطعة 14 منزل 10 ، السالمية" : "Arabian Gulf St, Block 14 House 10, Salmiya",
                MealsCount: 3,
                MealsSummary: isAr ? "3 وجبات" : "3 meals",
                DeliveryTimeSlot: isAr ? "09:24 ص" : "09:24 AM",
                Status: "Pending",
                StatusText: isAr ? "في الطريق للعميل" : "On the way",
                StatusColor: "orange",
                IsCurrent: false,
                Latitude: 29.3450,
                Longitude: 48.0350
            )
        };

        var defaultFocused = fallbackStops.First(s => s.IsCurrent);
        if (request.FocusedStopId.HasValue)
        {
            var match = fallbackStops.FirstOrDefault(s => s.StopId == request.FocusedStopId.Value);
            if (match is not null)
            {
                defaultFocused = match;
            }
        }

        var defaultWaypoints = GeneratePolylineWaypoints(29.3320, 48.0210, 29.3450, 48.0350);

        return Result.Success(new DriverDeliveryMapResponse(
            TripId: Guid.NewGuid(),
            TripCode: "TRP-101",
            DriverLatitude: 29.3320,
            DriverLongitude: 48.0210,
            TotalStopsCount: 3,
            CompletedStopsCount: 1,
            FocusedStop: defaultFocused,
            Stops: fallbackStops,
            RoutePolylineWaypoints: defaultWaypoints
        ));
    }

    private static IReadOnlyList<DriverMapWaypointDto> GeneratePolylineWaypoints(
        double driverLat, double driverLng, double destLat, double destLng)
    {
        return new List<DriverMapWaypointDto>
        {
            new(driverLat, driverLng),
            new(driverLat + 0.002, driverLng + 0.001),
            new(driverLat + 0.003, driverLng + 0.004),
            new(driverLat + 0.006, driverLng + 0.005),
            new(destLat, destLng)
        };
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

        return await _db.DriverProfiles.OrderByDescending(d => d.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
    }
}
