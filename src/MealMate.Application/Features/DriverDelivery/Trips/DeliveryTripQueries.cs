using System.Globalization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Trips;

public sealed record GetRestaurantTripsQuery(
    Guid RestaurantId,
    string? Filter = null,
    string? Search = null) : IQuery<RestaurantTripsDataDto>;

public sealed record GetRestaurantTripDetailQuery(
    Guid TripId) : IQuery<DeliveryTripDto?>;

public sealed class GetRestaurantTripsQueryHandler : IQueryHandler<GetRestaurantTripsQuery, RestaurantTripsDataDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetRestaurantTripsQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<RestaurantTripsDataDto>> Handle(
        GetRestaurantTripsQuery request,
        CancellationToken cancellationToken)
    {
        // Real scoping: only the signed-in restaurant's own profile is read.
        // Callers without a restaurant profile get an empty workspace instead of
        // another tenant's data.
        var restaurantProfile = await _db.RestaurantProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == _currentUser.UserId, cancellationToken);

        if (restaurantProfile is null)
        {
            return Result.Success(EmptyWorkspace());
        }

        var restaurantId = restaurantProfile.Id;

        var allTrips = await _db.Trips
            .AsNoTracking()
            .Include(t => t.Stops)
            .Include(t => t.DriverAssignments)
            .Where(t => t.RestaurantId == restaurantId && !t.IsDeleted)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var driverProfileIds = allTrips
            .SelectMany(t => t.DriverAssignments)
            .Select(da => da.DriverProfileId)
            .Distinct()
            .ToList();

        var driversDict = await _db.DriverProfiles
            .AsNoTracking()
            .Where(d => driverProfileIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, cancellationToken);

        var activeCount = allTrips.Count(t => t.Status == TripStatus.InProgress);
        var assignedCount = allTrips.Count(t => t.Status == TripStatus.Assigned || t.Status == TripStatus.Planned);
        var completedCount = allTrips.Count(t => t.Status == TripStatus.Completed);

        var summaries = new List<TripSummaryDto>
        {
            new("active", new("قيد التنفيذ", "In progress"), activeCount),
            new("assigned", new("مسنَدة", "Assigned"), assignedCount),
            new("completed", new("مكتملة", "Completed"), completedCount)
        };

        var filteredList = allTrips.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(request.Filter) && !request.Filter.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var targetStatus = MapClientStatus(request.Filter);
            filteredList = filteredList.Where(t => t.Status == targetStatus);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var q = request.Search.Trim().ToLower();
            filteredList = filteredList.Where(t =>
                t.Code.ToLower().Contains(q) ||
                (t.ShiftName != null && t.ShiftName.ToLower().Contains(q)) ||
                (t.Notes != null && t.Notes.ToLower().Contains(q)) ||
                t.Stops.Any(s => s.OrderCode.ToLower().Contains(q) || s.CustomerMaskedId.ToLower().Contains(q)));
        }

        var tripDtos = filteredList
            .Select(t => BuildTripDto(t, driversDict, route: null, timeline: null, pickupAtByStop: null, stopEvidence: null))
            .ToList();

        var candidates = await BuildCandidatesAsync(restaurantProfile, cancellationToken);

        var result = new RestaurantTripsDataDto(
            new("رحلات التوصيل", "Delivery Trips"),
            new("جدولة وتوزيع رحلات التوصيل اليومية ومتابعة مسار السائقين مباشرة", "Schedule daily delivery trips and track driver routes in real-time"),
            new(WrapDateLabel(DateTime.UtcNow), WrapDateLabel(DateTime.UtcNow)),
            new("تأكد من تسليم الصناديق للسائق وتأكيد استلام الرحلة قبل مغادرته المطعم.", "Ensure boxes are handed over to the driver and pickup is confirmed before departure."),
            summaries,
            tripDtos,
            candidates);

        return Result.Success(result);
    }

    public static string ToClientStatus(TripStatus status) => status switch
    {
        TripStatus.Draft => "draft",
        TripStatus.Planned => "assigned",
        TripStatus.Assigned => "assigned",
        TripStatus.InProgress => "in_progress",
        TripStatus.Completed => "completed",
        TripStatus.Cancelled => "cancelled",
        _ => "assigned"
    };

    public static string ToClientStopStatus(TripStopStatus status) => status switch
    {
        TripStopStatus.Pending => "pending",
        TripStopStatus.PickedUp => "picked_up",
        TripStopStatus.Delivered => "delivered",
        TripStopStatus.Failed => "failed",
        TripStopStatus.ReturnedToKitchen => "returned_to_kitchen",
        _ => "pending"
    };

    public static TripStatus MapClientStatus(string status) => status.ToLowerInvariant() switch
    {
        "draft" => TripStatus.Draft,
        "assigned" => TripStatus.Assigned,
        "in_progress" => TripStatus.InProgress,
        "completed" => TripStatus.Completed,
        "cancelled" => TripStatus.Cancelled,
        _ => TripStatus.Assigned
    };

    internal static IReadOnlyDictionary<Guid, DateTime> ConfirmedHandoverByStop(
        IReadOnlyCollection<OrderHandover> handovers, Guid tripId)
        => handovers
            .Where(h => h.TripId == tripId && h.TripStopId.HasValue && h.HandedOverAtUtc.HasValue && h.RevokedAtUtc == null)
            .GroupBy(h => h.TripStopId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(h => h.HandedOverAtUtc).First().HandedOverAtUtc!.Value);

    internal static DeliveryTripDto BuildTripDto(
        Trip trip,
        IReadOnlyDictionary<Guid, DriverProfile> driversDict,
        TripRouteDto? route,
        IReadOnlyList<TripTimelineEventDto>? timeline,
        IReadOnlyDictionary<Guid, DateTime>? pickupAtByStop,
        IReadOnlyDictionary<Guid, StopRouteEvidence>? stopEvidence)
    {
        var orderedStops = trip.Stops
            .Where(s => !s.IsDeleted)
            .OrderBy(s => s.SequenceNumber)
            .ThenBy(s => s.Id)
            .ToList();

        var primaryAssignment = trip.DriverAssignments
            .Where(a => !a.IsDeleted)
            .OrderByDescending(a => a.AssignedAtUtc)
            .ThenByDescending(a => a.Id)
            .FirstOrDefault();
        DriverProfile? driver = null;
        if (primaryAssignment != null && driversDict.TryGetValue(primaryAssignment.DriverProfileId, out var dp))
        {
            driver = dp;
        }

        var stops = orderedStops
            .Select(s =>
            {
                StopRouteEvidence? evidence = null;
                if (stopEvidence is not null)
                {
                    stopEvidence.TryGetValue(s.Id, out evidence);
                }

                return new TripStopDto(
                    s.Id.ToString(),
                    s.OrderCode,
                    s.CustomerMaskedId,
                    new(s.ZoneNameAr, s.ZoneNameEn),
                    new(s.MealSummaryAr, s.MealSummaryEn),
                    string.IsNullOrWhiteSpace(s.FullAddress) ? null : new(s.FullAddress, s.FullAddress),
                    Coordinates(s.Latitude, s.Longitude),
                    s.BoxCount,
                    new(s.SlotLabel, s.SlotLabel),
                    ToClientStopStatus(s.Status),
                    EtaUtc: evidence?.EtaUtc,
                    DistanceMeters: evidence?.DistanceMeters,
                    ArrivedAtUtc: s.ArrivedAtUtc ?? s.StartedDeliveryAtUtc,
                    PickedUpAtUtc: pickupAtByStop is null || !pickupAtByStop.TryGetValue(s.Id, out var pickedUp) ? null : pickedUp,
                    DeliveredAtUtc: s.DeliveredAtUtc,
                    FailureReason: string.IsNullOrWhiteSpace(s.FailureReason) ? null : new(s.FailureReason, s.FailureReason));
            })
            .ToList();

        var zoneNamesAr = orderedStops.Select(s => s.ZoneNameAr).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
        var zoneNamesEn = orderedStops.Select(s => s.ZoneNameEn).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
        var zoneAr = zoneNamesAr.Count > 0 ? string.Join(" · ", zoneNamesAr) : string.Empty;
        var zoneEn = zoneNamesEn.Count > 0 ? string.Join(" · ", zoneNamesEn) : string.Empty;

        return new DeliveryTripDto(
            trip.Id.ToString(),
            trip.Code,
            ToClientStatus(trip.Status),
            string.IsNullOrWhiteSpace(trip.ShiftName) ? null : new(trip.ShiftName!, trip.ShiftName!),
            new(zoneAr, zoneEn),
            driver?.Id.ToString(),
            driver != null ? new(driver.FullNameAr, driver.FullNameEn) : null,
            driver?.VehiclePlate,
            trip.DispatcherUserId?.ToString(),
            null,
            stops,
            stops.Sum(s => s.BoxCount),
            stops.Count,
            new(FormatTimestamp(trip.CreatedAtUtc), FormatTimestamp(trip.CreatedAtUtc)),
            trip.StartedAtUtc.HasValue ? new(FormatTimeOnly(trip.StartedAtUtc.Value), FormatTimeOnly(trip.StartedAtUtc.Value)) : null,
            trip.CompletedAtUtc.HasValue ? new(FormatTimeOnly(trip.CompletedAtUtc.Value), FormatTimeOnly(trip.CompletedAtUtc.Value)) : null,
            !string.IsNullOrWhiteSpace(trip.Notes) ? new(trip.Notes, trip.Notes) : null,
            route,
            timeline);
    }

    /// <summary>
    /// Ready-to-dispatch candidates: the restaurant's daily orders that are not
    /// yet part of any trip stop of this restaurant, enriched only with real
    /// persisted data. Values the restaurant domain does not carry stay null —
    /// the UI renders em dashes instead of fabricating labels.
    /// </summary>
    private async Task<IReadOnlyList<TripCandidateStopDto>> BuildCandidatesAsync(
        RestaurantProfile restaurantProfile,
        CancellationToken cancellationToken)
    {
        var assignedDailyOrderIds = await _db.TripStops
            .AsNoTracking()
            .Where(s => !s.IsDeleted && s.DailyOrderId.HasValue && s.Trip.RestaurantId == restaurantProfile.Id)
            .Select(s => s.DailyOrderId!.Value)
            .ToListAsync(cancellationToken);

        var candidateOrders = await _db.RestaurantDailyOrders
            .AsNoTracking()
            .Where(o => o.RestaurantId == restaurantProfile.Id
                && !o.IsDeleted
                && !assignedDailyOrderIds.Contains(o.Id)
                && o.Status != OrderStatus.Cancelled)
            .OrderByDescending(o => o.ServiceDate)
            .ThenByDescending(o => o.CreatedAtUtc)
            .Take(15)
            .ToListAsync(cancellationToken);

        if (candidateOrders.Count == 0)
        {
            return [];
        }

        var dayIds = candidateOrders.Select(o => o.SubscriptionDayId).Distinct().ToList();
        var days = await _db.SubscriptionDays
            .AsNoTracking()
            .Where(d => dayIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, cancellationToken);

        var regionIds = days.Values
            .Select(d => d.DeliveryRegionId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var regions = regionIds.Count > 0
            ? await _db.Regions
                .AsNoTracking()
                .Where(r => regionIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, cancellationToken)
            : new Dictionary<Guid, Region>();

        var addressIds = days.Values
            .Select(d => d.DeliveryAddressId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var addresses = addressIds.Count > 0
            ? await _db.CustomerAddresses
                .AsNoTracking()
                .Where(a => addressIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, cancellationToken)
            : new Dictionary<Guid, CustomerAddress>();

        var subscriptionIds = days.Values
            .Select(d => d.SubscriptionId)
            .Distinct()
            .ToList();
        var subscriptions = await _db.Subscriptions
            .AsNoTracking()
            .Where(s => subscriptionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var candidates = new List<TripCandidateStopDto>(candidateOrders.Count);
        foreach (var order in candidateOrders)
        {
            days.TryGetValue(order.SubscriptionDayId, out var day);

            LocalizedTextDto? zoneLabel = null;
            LocalizedTextDto? addressLabel = null;
            GeoPointDto? coordinates = null;
            string? customerMaskedId = null;
            Guid? customerUserId = null;

            if (day is not null)
            {
                if (day.DeliveryRegionId.HasValue && regions.TryGetValue(day.DeliveryRegionId.Value, out var region))
                {
                    zoneLabel = new(region.NameAr, region.NameEn);
                }

                var addressLine = !string.IsNullOrWhiteSpace(day.DeliveryAddressLine)
                    ? day.DeliveryAddressLine
                    : day.DeliveryAddressId.HasValue
                        && addresses.TryGetValue(day.DeliveryAddressId.Value, out var address)
                        && !string.IsNullOrWhiteSpace(address.AddressLine)
                            ? address.AddressLine
                            : null;
                if (addressLine is not null)
                {
                    addressLabel = new(addressLine, addressLine);
                }

                if (day.DeliveryAddressId.HasValue && addresses.TryGetValue(day.DeliveryAddressId.Value, out var addr))
                {
                    coordinates = Coordinates(addr.Latitude, addr.Longitude);
                }

                if (subscriptions.TryGetValue(day.SubscriptionId, out var subscription))
                {
                    customerUserId = subscription.CustomerUserId;
                }
            }

            if (customerUserId.HasValue)
            {
                // Deterministic masked alias derived from the real customer
                // identity byte content; no counter and no invented code.
                var suffix = customerUserId.Value.ToByteArray()[^1] % 100;
                customerMaskedId = $"CUST-**{suffix:D2}";
            }

            candidates.Add(new(
                order.Id.ToString(),
                OrderCode: null,
                CustomerMaskedId: customerMaskedId,
                ZoneLabel: zoneLabel,
                MealSummary: null,
                order.BoxCount,
                SlotLabel: null,
                ShiftLabel: null,
                Address: addressLabel,
                Coordinates: coordinates));
        }

        return candidates;
    }

    private static RestaurantTripsDataDto EmptyWorkspace() => new(
        new("رحلات التوصيل", "Delivery Trips"),
        new("جدولة وتوزيع رحلات التوصيل اليومية ومتابعة مسار السائقين مباشرة", "Schedule daily delivery trips and track driver routes in real-time"),
        new(WrapDateLabel(DateTime.UtcNow), WrapDateLabel(DateTime.UtcNow)),
        new("تأكد من تسليم الصناديق للسائق وتأكيد استلام الرحلة قبل مغادرته المطعم.", "Ensure boxes are handed over to the driver and pickup is confirmed before departure."),
        [],
        [],
        []);

    private static string WrapDateLabel(DateTime utc) => $"Today, {utc:yyyy-MM-dd}";

    private static GeoPointDto? Coordinates(decimal? latitude, decimal? longitude)
        => latitude is >= -90m and <= 90m && longitude is >= -180m and <= 180m
            ? new GeoPointDto((double)latitude.Value, (double)longitude.Value)
            : null;

    private static string FormatTimestamp(DateTime? utc)
        => utc?.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatTimeOnly(DateTime utc)
        => utc.ToString("HH:mm", CultureInfo.InvariantCulture);
}

public sealed class GetRestaurantTripDetailQueryHandler : IQueryHandler<GetRestaurantTripDetailQuery, DeliveryTripDto?>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ITripRouteBuilder _routeBuilder;
    private readonly ITripTimelineBuilder _timelineBuilder;

    public GetRestaurantTripDetailQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ITripRouteBuilder routeBuilder,
        ITripTimelineBuilder timelineBuilder)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _routeBuilder = routeBuilder;
        _timelineBuilder = timelineBuilder;
    }

    public async Task<Result<DeliveryTripDto?>> Handle(
        GetRestaurantTripDetailQuery request,
        CancellationToken cancellationToken)
    {
        // Secure scoping: a trip is readable only by its owning restaurant staff.
        var restaurantProfile = await _db.RestaurantProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == _currentUser.UserId, cancellationToken);
        if (restaurantProfile is null)
        {
            return Result.Success<DeliveryTripDto?>(null);
        }

        var trip = await _db.Trips
            .AsNoTracking()
            .Include(t => t.Stops)
            .Include(t => t.DriverAssignments)
            .FirstOrDefaultAsync(t => t.Id == request.TripId
                && !t.IsDeleted
                && t.RestaurantId == restaurantProfile.Id, cancellationToken);
        if (trip is null)
        {
            return Result.Success<DeliveryTripDto?>(null);
        }

        var orderedStops = trip.Stops
            .Where(s => !s.IsDeleted)
            .OrderBy(s => s.SequenceNumber)
            .ThenBy(s => s.Id)
            .ToList();

        var assignments = trip.DriverAssignments
            .Where(a => !a.IsDeleted)
            .OrderByDescending(a => a.AssignedAtUtc)
            .ThenByDescending(a => a.Id)
            .ToList();

        // Every driver recorded in this trip's evidence (assignments, scans,
        // handovers, tracking points) is resolved once for labels.
        var nowUtc = _clock.UtcNow;
        var evidenceDriverIds = assignments.Select(a => a.DriverProfileId).ToList();

        var latestTracking = await _db.DeliveryTrackingPoints
            .AsNoTracking()
            .Where(pt => pt.TripId == trip.Id && pt.RecordedAtUtc <= nowUtc)
            .OrderByDescending(pt => pt.RecordedAtUtc)
            .ThenByDescending(pt => pt.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (latestTracking != null)
        {
            evidenceDriverIds.Add(latestTracking.DriverProfileId);
        }

        var pickupScans = await _db.PickupScans
            .AsNoTracking()
            .Where(ps => ps.TripId == trip.Id)
            .ToListAsync(cancellationToken);
        evidenceDriverIds.AddRange(pickupScans.Select(s => s.DriverProfileId));

        var handovers = await _db.OrderHandovers
            .AsNoTracking()
            .Where(h => h.TripId == trip.Id && !h.IsDeleted)
            .ToListAsync(cancellationToken);
        evidenceDriverIds.AddRange(handovers.Where(h => h.DriverProfileId.HasValue).Select(h => h.DriverProfileId!.Value));

        var driverNames = evidenceDriverIds.Count > 0
            ? await _db.DriverProfiles
                .AsNoTracking()
                .Where(d => evidenceDriverIds.Distinct().Contains(d.Id))
                .ToDictionaryAsync(
                    d => d.Id,
                    d => !string.IsNullOrWhiteSpace(d.FullNameEn) ? d.FullNameEn : d.FullNameAr,
                    cancellationToken)
            : new Dictionary<Guid, string>();

        var routeResult = await _routeBuilder.BuildAsync(
            new TripRouteRequest(
                trip,
                orderedStops,
                restaurantProfile.Latitude,
                restaurantProfile.Longitude,
                latestTracking,
                DriverLocationStale: false),
            cancellationToken);

        var timeline = _timelineBuilder.Build(new TripTimelineRequest(
            trip,
            orderedStops,
            assignments,
            pickupScans,
            handovers,
            driverNames));

        var pickupAtByStop = GetRestaurantTripsQueryHandler.ConfirmedHandoverByStop(handovers, trip.Id);

        var driversDict = await _db.DriverProfiles
            .AsNoTracking()
            .Where(d => assignments.Select(a => a.DriverProfileId).Distinct().Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, cancellationToken);

        var route = routeResult.Route;
        var dto = GetRestaurantTripsQueryHandler.BuildTripDto(
            trip,
            driversDict,
            route,
            timeline,
            pickupAtByStop,
            routeResult.StopEvidence);

        return Result.Success<DeliveryTripDto?>(dto);
    }
}
