using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
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
        if (currentTrips.Length == 0) return new DriverMapLocationPageDto(_clock.UtcNow, []);

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
        var pointByPair = points.Where(point => authorizedPairs.Contains((point.TripId, point.DriverProfileId)))
            .GroupBy(point => (point.TripId, point.DriverProfileId))
            .ToDictionary(group => group.Key, group => group.OrderBy(point => point.Id).First());
        var items = currentTrips.OrderBy(trip => trip.Trip.Code, StringComparer.OrdinalIgnoreCase).ThenBy(trip => trip.Trip.Id)
            .Select(trip =>
            {
                pointByPair.TryGetValue((trip.Trip.Id, trip.DriverProfileId!.Value), out var point);
                return new DriverMapLocationDto(trip.DriverProfileId.Value, trip.Trip.RestaurantId, trip.Trip.Id,
                    ValidPoint(point?.Latitude, point?.Longitude), point?.RecordedAtUtc, point?.Heading, point?.SpeedKmh);
            }).ToArray();
        return new DriverMapLocationPageDto(_clock.UtcNow, items);
    }

    private static MapPointDto? ValidPoint(decimal? latitude, decimal? longitude)
        => latitude is >= -90m and <= 90m && longitude is >= -180m and <= 180m
            ? new MapPointDto(latitude.Value, longitude.Value)
            : null;
}
