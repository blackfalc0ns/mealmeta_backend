using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.AdminCoverage;

public sealed record GetAdminDriverMapLocationsQuery(
    string CountryCode,
    DateOnly ServiceDate,
    Guid? GovernorateId,
    Guid? RestaurantId,
    string? Search = null) : IQuery<DriverMapLocationPageDto>;

public sealed class GetAdminDriverMapLocationsQueryHandler : IQueryHandler<GetAdminDriverMapLocationsQuery, DriverMapLocationPageDto>
{
    private readonly IAppDbContext _db;
    private readonly AdminCoverageAccess _access;
    private readonly IDateTimeProvider _clock;

    public GetAdminDriverMapLocationsQueryHandler(IAppDbContext db, AdminCoverageAccess access, IDateTimeProvider clock)
        => (_db, _access, _clock) = (db, access, clock);

    public async Task<Result<DriverMapLocationPageDto>> Handle(GetAdminDriverMapLocationsQuery request, CancellationToken cancellationToken)
    {
        if (request.ServiceDate == default) return Error.Validation("admin_coverage.service_date_required");
        var accessResult = await _access.ResolveAsync(request.CountryCode, cancellationToken, PermissionCodes.AdminCoverageOperationsView);
        if (accessResult.IsFailure) return accessResult.Error;
        var access = accessResult.Value;
        var regionById = access.Regions.ToDictionary(region => region.Id);
        if (request.GovernorateId is Guid governorateId)
        {
            var selectable = access.GetAuthorizedRegionIds();
            var governorate = access.Regions.FirstOrDefault(region => region.Id == governorateId && region.Type == RegionType.Governorate);
            if (governorate is null || !selectable.Contains(governorateId) || !governorate.IsActive)
                return Error.Forbidden("admin_coverage.governorate_not_allowed");
        }

        var authorizedTrips = await AdminCoverageTripScope.LoadAsync(_db, access, request.ServiceDate, null, request.RestaurantId, null, cancellationToken);
        IEnumerable<AdminCoverageTripRecord> filtered = authorizedTrips;
        if (request.GovernorateId is Guid selectedGovernorate)
            filtered = filtered.Where(trip => AdminCoverageTripScope.AllStopsBelongToGovernorate(trip, selectedGovernorate, regionById));
        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
            filtered = filtered.Where(trip => trip.Trip.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
                || trip.RestaurantNameAr.Contains(search, StringComparison.OrdinalIgnoreCase)
                || trip.RestaurantNameEn.Contains(search, StringComparison.OrdinalIgnoreCase)
                || trip.Stops.Any(stop => stop.OrderCode.Contains(search, StringComparison.OrdinalIgnoreCase)));
        var currentTrips = filtered.Where(trip => trip.DriverProfileId is not null).ToArray();

        var pointByPair = new Dictionary<(Guid TripId, Guid DriverId), DeliveryTrackingPoint>();
        if (currentTrips.Length > 0)
        {
            var tripIds = currentTrips.Select(trip => trip.Trip.Id).Distinct().ToArray();
            var driverIds = currentTrips.Select(trip => trip.DriverProfileId!.Value).Distinct().ToArray();
            var authorizedPairs = currentTrips.Select(trip => (TripId: trip.Trip.Id, DriverId: trip.DriverProfileId!.Value)).ToHashSet();
            var latestPoints =
                from point in _db.DeliveryTrackingPoints.AsNoTracking()
                where tripIds.Contains(point.TripId) && driverIds.Contains(point.DriverProfileId)
                group point by new { point.TripId, point.DriverProfileId } into grouped
                select new { grouped.Key.TripId, grouped.Key.DriverProfileId, RecordedAtUtc = grouped.Max(point => point.RecordedAtUtc) };
            var points = await (
                from point in _db.DeliveryTrackingPoints.AsNoTracking()
                join latest in latestPoints
                    on new { point.TripId, point.DriverProfileId, point.RecordedAtUtc }
                    equals new { latest.TripId, latest.DriverProfileId, latest.RecordedAtUtc }
                select point).ToListAsync(cancellationToken);
            pointByPair = points.Where(point => authorizedPairs.Contains((point.TripId, point.DriverProfileId)))
                .GroupBy(point => (point.TripId, point.DriverProfileId))
                .ToDictionary(group => group.Key, group => group.OrderBy(point => point.Id).First());
        }

        var items = currentTrips.OrderBy(trip => trip.Trip.Code, StringComparer.OrdinalIgnoreCase).ThenBy(trip => trip.Trip.Id)
            .Select(trip =>
            {
                pointByPair.TryGetValue((trip.Trip.Id, trip.DriverProfileId!.Value), out var point);
                return new DriverMapLocationDto(trip.DriverProfileId.Value, trip.Trip.RestaurantId, trip.Trip.Id,
                    ValidPoint(point?.Latitude, point?.Longitude), point?.RecordedAtUtc, point?.Heading, point?.SpeedKmh);
            }).ToArray();

        var activeSessions = await _db.DriverWorkSessions.AsNoTracking()
            .Where(s => s.Status == "Active" && !s.IsDeleted)
            .OrderByDescending(s => s.StartedAtUtc)
            .ToListAsync(cancellationToken);

        var sessionByDriverId = activeSessions
            .GroupBy(s => s.DriverProfileId)
            .ToDictionary(g => g.Key, g => g.First());

        var onDutyDriverIds = sessionByDriverId.Keys.ToArray();
        var roster = new List<AdminCoverageDriverRosterDto>();

        if (onDutyDriverIds.Length > 0)
        {
            var driverProfiles = await _db.DriverProfiles.AsNoTracking()
                .Where(d => onDutyDriverIds.Contains(d.Id) && !d.IsDeleted)
                .ToListAsync(cancellationToken);

            var tripsByDriver = currentTrips
                .GroupBy(t => t.DriverProfileId!.Value)
                .ToDictionary(g => g.Key, g => g.ToArray());

            var activeAssignments = await _db.DriverAssignments.AsNoTracking()
                .Include(da => da.Trip)
                .ThenInclude(t => t.Stops)
                .Where(da => onDutyDriverIds.Contains(da.DriverProfileId)
                             && da.Status == "Active"
                             && !da.IsDeleted
                             && !da.Trip.IsDeleted
                             && (da.Trip.Status == TripStatus.InProgress || da.Trip.Status == TripStatus.Assigned))
                .OrderByDescending(da => da.AssignedAtUtc)
                .ThenByDescending(da => da.Id)
                .ToListAsync(cancellationToken);

            var activeAssignmentByDriver = activeAssignments
                .GroupBy(da => da.DriverProfileId)
                .ToDictionary(g => g.Key, g => g.First());

            var activeTripIdsToLoad = activeAssignments
                .Select(a => a.TripId)
                .Where(tripId => !currentTrips.Any(ct => ct.Trip.Id == tripId))
                .Distinct()
                .ToArray();

            if (activeTripIdsToLoad.Length > 0)
            {
                var activePairs = activeAssignments
                    .Where(a => activeTripIdsToLoad.Contains(a.TripId))
                    .Select(a => (TripId: a.TripId, DriverId: a.DriverProfileId))
                    .ToHashSet();

                var latestActivePoints =
                    from point in _db.DeliveryTrackingPoints.AsNoTracking()
                    where activeTripIdsToLoad.Contains(point.TripId) && onDutyDriverIds.Contains(point.DriverProfileId)
                    group point by new { point.TripId, point.DriverProfileId } into grouped
                    select new { grouped.Key.TripId, grouped.Key.DriverProfileId, RecordedAtUtc = grouped.Max(point => point.RecordedAtUtc) };

                var extraPoints = await (
                    from point in _db.DeliveryTrackingPoints.AsNoTracking()
                    join latest in latestActivePoints
                        on new { point.TripId, point.DriverProfileId, point.RecordedAtUtc }
                        equals new { latest.TripId, latest.DriverProfileId, latest.RecordedAtUtc }
                    select point).ToListAsync(cancellationToken);

                foreach (var group in extraPoints.Where(point => activePairs.Contains((point.TripId, point.DriverProfileId)))
                             .GroupBy(point => (point.TripId, point.DriverProfileId)))
                {
                    pointByPair[group.Key] = group.OrderBy(point => point.Id).First();
                }
            }

            var now = _clock.UtcNow;

            foreach (var driver in driverProfiles)
            {
                if (driver.CountryId != null && driver.CountryId != access.Country.Id)
                    continue;

                var session = sessionByDriverId[driver.Id];
                var driverRestaurantId = session.RestaurantId ?? driver.RestaurantId;
                tripsByDriver.TryGetValue(driver.Id, out var driverAssignedTrips);
                driverAssignedTrips ??= [];

                if (request.RestaurantId is Guid filterRestaurantId)
                {
                    var matchesRestaurant = driverRestaurantId == filterRestaurantId
                        || driverAssignedTrips.Any(t => t.Trip.RestaurantId == filterRestaurantId);
                    if (!matchesRestaurant) continue;
                }

                if (!access.IsSuperAdmin)
                {
                    var isAuthorized = access.Grants.Any(grant =>
                        grant.IsCountryWide
                        || (grant.RestaurantId is Guid rId && (driverRestaurantId == rId || driverAssignedTrips.Any(t => t.Trip.RestaurantId == rId)))
                        || (grant.RegionId is Guid regId && (
                            (driver.RegionId.HasValue && CoverageAccessContext.IsDescendantOrSelf(regionById.GetValueOrDefault(driver.RegionId.Value), regId, regionById))
                            || driverAssignedTrips.Any(t => t.StopRegionIds.Any(sReg => sReg.HasValue && CoverageAccessContext.IsDescendantOrSelf(regionById.GetValueOrDefault(sReg.Value), regId, regionById)))
                        )));
                    if (!isAuthorized) continue;
                }

                if (request.GovernorateId is Guid filterGovId)
                {
                    if (driverAssignedTrips.Length > 0)
                    {
                        if (!driverAssignedTrips.Any(t => AdminCoverageTripScope.AllStopsBelongToGovernorate(t, filterGovId, regionById)))
                            continue;
                    }
                    else
                    {
                        if (!driver.RegionId.HasValue || !CoverageAccessContext.IsDescendantOrSelf(regionById.GetValueOrDefault(driver.RegionId.Value), filterGovId, regionById))
                            continue;
                    }
                }

                if (!string.IsNullOrEmpty(search))
                {
                    var matchesSearch = driver.FullNameAr.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || driver.FullNameEn.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || driverAssignedTrips.Any(t => t.Trip.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
                            || t.RestaurantNameAr.Contains(search, StringComparison.OrdinalIgnoreCase)
                            || t.RestaurantNameEn.Contains(search, StringComparison.OrdinalIgnoreCase)
                            || t.Stops.Any(s => s.OrderCode.Contains(search, StringComparison.OrdinalIgnoreCase)));
                    if (!matchesSearch) continue;
                }

                string? profileImageUrl = null;
                if (driver.ProfileImageStatus == VerificationStatus.Approved && !string.IsNullOrWhiteSpace(driver.ProfileImageStorageKey))
                {
                    profileImageUrl = $"/api/v1/uploads/content?key={Uri.EscapeDataString(driver.ProfileImageStorageKey)}";
                }

                Guid? currentTripId = null;
                string? currentTripStatus = null;
                string? nextZoneNameAr = null;
                string? nextZoneNameEn = null;
                MapPointDto? currentPoint = null;
                DateTime? locationRecordedAtUtc = null;

                if (activeAssignmentByDriver.TryGetValue(driver.Id, out var activeAssignment))
                {
                    var ongoingTrip = activeAssignment.Trip;
                    var isOngoingTripAuthorized = access.IsSuperAdmin || access.Grants.Any(grant =>
                        grant.IsCountryWide
                        || (grant.RestaurantId is Guid rId && ongoingTrip.RestaurantId == rId)
                        || (grant.RegionId is Guid regId && driver.RegionId.HasValue && CoverageAccessContext.IsDescendantOrSelf(regionById.GetValueOrDefault(driver.RegionId.Value), regId, regionById)));

                    if (request.RestaurantId is Guid reqRestId && ongoingTrip.RestaurantId != reqRestId)
                    {
                        isOngoingTripAuthorized = false;
                    }

                    if (isOngoingTripAuthorized)
                    {
                        currentTripId = ongoingTrip.Id;
                        currentTripStatus = ongoingTrip.Status.ToString();

                        var actionableStop = ongoingTrip.Stops
                            .Where(s => !s.IsDeleted && (s.Status == TripStopStatus.PickedUp || s.Status == TripStopStatus.Pending))
                            .OrderBy(s => s.SequenceNumber)
                            .ThenBy(s => s.Id)
                            .FirstOrDefault();

                        if (actionableStop != null)
                        {
                            nextZoneNameAr = actionableStop.ZoneNameAr;
                            nextZoneNameEn = actionableStop.ZoneNameEn;
                        }

                        if (pointByPair.TryGetValue((ongoingTrip.Id, driver.Id), out var point))
                        {
                            var validPt = ValidPoint(point.Latitude, point.Longitude);
                            if (validPt != null && point.RecordedAtUtc <= now.AddSeconds(30))
                            {
                                currentPoint = validPt;
                                locationRecordedAtUtc = point.RecordedAtUtc;
                            }
                        }
                    }
                }

                var tripDtos = driverAssignedTrips
                    .OrderBy(t => t.Trip.Code, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(t => t.Trip.Id)
                    .Select(t => new AdminCoverageDriverTripDto(
                        t.Trip.Id,
                        t.Trip.Code,
                        t.Trip.PlannedDate,
                        t.Trip.Status.ToString(),
                        CalculateBoxTotals(t.Stops)))
                    .ToArray();

                roster.Add(new AdminCoverageDriverRosterDto(
                    driver.Id,
                    driverRestaurantId,
                    driver.FullNameAr,
                    driver.FullNameEn,
                    profileImageUrl,
                    session.StartedAtUtc,
                    currentPoint,
                    locationRecordedAtUtc,
                    tripDtos,
                    driver.IsAvailable,
                    currentTripId,
                    currentTripStatus,
                    nextZoneNameAr,
                    nextZoneNameEn));
            }
        }

        return new DriverMapLocationPageDto(
            _clock.UtcNow,
            items,
            roster.OrderBy(d => d.FullNameEn, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.DriverId).ToArray());
    }

    private static DriverBoxTotalsDto CalculateBoxTotals(IReadOnlyList<TripStop> stops)
    {
        var stopCount = stops.Count;
        var deliveredStopCount = 0;
        var remainingStopCount = 0;
        var failedStopCount = 0;
        var returnedStopCount = 0;

        var deliveredBoxCount = 0;
        var remainingBoxCount = 0;
        var failedBoxCount = 0;
        var returnedBoxCount = 0;
        var invalidBoxCount = 0;

        foreach (var stop in stops)
        {
            switch (stop.Status)
            {
                case TripStopStatus.Delivered:
                    deliveredStopCount++;
                    break;
                case TripStopStatus.Pending:
                case TripStopStatus.PickedUp:
                    remainingStopCount++;
                    break;
                case TripStopStatus.Failed:
                    failedStopCount++;
                    break;
                case TripStopStatus.ReturnedToKitchen:
                    returnedStopCount++;
                    break;
            }

            if (stop.BoxCount <= 0)
            {
                invalidBoxCount++;
            }
            else
            {
                switch (stop.Status)
                {
                    case TripStopStatus.Delivered:
                        deliveredBoxCount += stop.BoxCount;
                        break;
                    case TripStopStatus.Pending:
                    case TripStopStatus.PickedUp:
                        remainingBoxCount += stop.BoxCount;
                        break;
                    case TripStopStatus.Failed:
                        failedBoxCount += stop.BoxCount;
                        break;
                    case TripStopStatus.ReturnedToKitchen:
                        returnedBoxCount += stop.BoxCount;
                        break;
                }
            }
        }

        return new DriverBoxTotalsDto(
            stopCount,
            deliveredStopCount,
            remainingStopCount,
            failedStopCount,
            returnedStopCount,
            deliveredBoxCount,
            remainingBoxCount,
            failedBoxCount,
            returnedBoxCount,
            invalidBoxCount);
    }

    private static MapPointDto? ValidPoint(decimal? latitude, decimal? longitude)
        => latitude is >= -90m and <= 90m && longitude is >= -180m and <= 180m
            ? new MapPointDto(latitude.Value, longitude.Value)
            : null;
}
