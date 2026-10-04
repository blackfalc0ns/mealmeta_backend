using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace MealMate.Application.Features.AdminCoverage;

public sealed record GetAdminTripPlannedRouteQuery(string CountryCode, Guid TripId) : IQuery<PlannedTripRouteDto>;

public sealed class GetAdminTripPlannedRouteQueryHandler : IQueryHandler<GetAdminTripPlannedRouteQuery, PlannedTripRouteDto>
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private readonly IAppDbContext _db;
    private readonly AdminCoverageAccess _access;
    private readonly IGoogleRoutingService _routing;
    private readonly IMemoryCache _cache;
    private readonly IDateTimeProvider _clock;

    public GetAdminTripPlannedRouteQueryHandler(IAppDbContext db, AdminCoverageAccess access, IGoogleRoutingService routing, IMemoryCache cache, IDateTimeProvider clock)
        => (_db, _access, _routing, _cache, _clock) = (db, access, routing, cache, clock);

    public async Task<Result<PlannedTripRouteDto>> Handle(GetAdminTripPlannedRouteQuery request, CancellationToken cancellationToken)
    {
        if (request.TripId == Guid.Empty) return Error.NotFound("admin_coverage.trip_not_found");
        var access = await _access.ResolveAsync(request.CountryCode, cancellationToken, PermissionCodes.AdminCoverageOperationsView);
        if (access.IsFailure) return access.Error;
        var records = await AdminCoverageTripScope.LoadAsync(_db, access.Value, null, request.TripId, null, null, cancellationToken);
        var record = records.SingleOrDefault();
        if (record is null) return Error.NotFound("admin_coverage.trip_not_found");

        var orderedStops = record.Stops.OrderBy(stop => stop.SequenceNumber).ThenBy(stop => stop.Id).ToArray();
        var cacheFingerprint = BuildFingerprint(request.TripId, record.RestaurantLatitude, record.RestaurantLongitude, orderedStops);
        var cacheKey = $"admin-coverage:planned-route:{request.TripId:N}:{cacheFingerprint}";
        if (_cache.TryGetValue(cacheKey, out PlannedTripRouteDto? cached) && cached is not null) return cached;

        var waypoints = new List<(Guid? StopId, MapPointDto? Point)>
        {
            (null, ValidPoint(record.RestaurantLatitude, record.RestaurantLongitude)),
        };
        waypoints.AddRange(orderedStops.Select(stop => (StopId: (Guid?)stop.Id, Point: ValidPoint(stop.Latitude, stop.Longitude))));
        var missingStopIds = orderedStops.Where(stop => ValidPoint(stop.Latitude, stop.Longitude) is null).Select(stop => stop.Id).ToArray();
        var legs = new List<PlannedTripRouteLegDto>();
        long totalDistanceMeters = 0;
        long totalDurationSeconds = 0;
        var hasAnyMetric = false;
        var hasFallback = false;
        string? errorCode = null;

        for (var index = 0; index < waypoints.Count - 1; index++)
        {
            var origin = waypoints[index];
            var destination = waypoints[index + 1];
            if (origin.Point is null || destination.Point is null)
            {
                var missingCode = origin.Point is null ? index == 0 ? "restaurant_origin_coordinates_missing" : "stop_coordinates_missing" : "stop_coordinates_missing";
                errorCode ??= missingCode;
                legs.Add(new PlannedTripRouteLegDto(index, origin.StopId, destination.StopId, origin.Point, destination.Point,
                    null, null, null, false, missingCode));
                continue;
            }

            var route = await _routing.GetDirectionsAsync(origin.Point.Latitude, origin.Point.Longitude,
                destination.Point.Latitude, destination.Point.Longitude, cancellationToken);
            if (route.IsFailure)
            {
                errorCode ??= route.Error.Code;
                legs.Add(new PlannedTripRouteLegDto(index, origin.StopId, destination.StopId, origin.Point, destination.Point,
                    null, null, null, false, route.Error.Code));
                continue;
            }

            var directions = route.Value;
            var providerPolyline = !directions.IsFallbackEstimate && !string.IsNullOrWhiteSpace(directions.OverviewPolyline)
                ? directions.OverviewPolyline
                : null;
            hasFallback |= directions.IsFallbackEstimate;
            totalDistanceMeters += directions.DistanceMeters;
            totalDurationSeconds += directions.DurationSeconds;
            hasAnyMetric = true;
            legs.Add(new PlannedTripRouteLegDto(index, origin.StopId, destination.StopId, origin.Point, destination.Point,
                providerPolyline, directions.DistanceMeters, directions.DurationSeconds, directions.IsFallbackEstimate,
                directions.IsFallbackEstimate ? "fallback_estimate_no_road_geometry" : providerPolyline is null ? "provider_geometry_missing" : null));
        }

        if (ValidPoint(record.RestaurantLatitude, record.RestaurantLongitude) is null && errorCode is null)
            errorCode = "restaurant_origin_coordinates_missing";
        var completeProviderRoute = legs.Count == orderedStops.Length && missingStopIds.Length == 0
            && legs.All(leg => !leg.IsFallbackEstimate && leg.EncodedPolyline is not null && leg.ErrorCode is null);
        var result = new PlannedTripRouteDto(request.TripId, _clock.UtcNow, "google-directions", legs, missingStopIds,
            completeProviderRoute && hasAnyMetric ? totalDistanceMeters : null,
            completeProviderRoute && hasAnyMetric ? totalDurationSeconds : null, hasFallback,
            !completeProviderRoute,
            errorCode);
        _cache.Set(cacheKey, result, CacheDuration);
        return result;
    }

    private static string BuildFingerprint(Guid tripId, decimal? latitude, decimal? longitude, IReadOnlyList<MealMate.Domain.DriverDelivery.TripStop> stops)
    {
        var key = new StringBuilder(tripId.ToString("N"));
        key.Append('|').Append(latitude?.ToString(CultureInfo.InvariantCulture)).Append('|').Append(longitude?.ToString(CultureInfo.InvariantCulture));
        foreach (var stop in stops)
            key.Append('|').Append(stop.SequenceNumber).Append(':').Append(stop.Id.ToString("N"))
                .Append(':').Append(stop.Latitude?.ToString(CultureInfo.InvariantCulture)).Append(',').Append(stop.Longitude?.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString())));
    }

    private static MapPointDto? ValidPoint(decimal? latitude, decimal? longitude)
        => latitude is >= -90m and <= 90m && longitude is >= -180m and <= 180m
            ? new MapPointDto(latitude.Value, longitude.Value)
            : null;
}
