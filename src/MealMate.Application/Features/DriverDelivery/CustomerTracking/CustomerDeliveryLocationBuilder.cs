using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Domain.DriverDelivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MealMate.Application.Features.DriverDelivery.DriverApp;

namespace MealMate.Application.Features.DriverDelivery.CustomerTracking;

public sealed record CustomerDeliveryLocationRequest(
    Guid DriverProfileId,
    Guid TripId,
    Guid TripStopId,
    bool IsArrived,
    bool IsTerminal,
    bool HasEarlierUndeliveredStops,
    decimal? DestinationLatitude,
    decimal? DestinationLongitude);

/// <summary>Driver location and ETA evidence for one box. Nulls mean "not claimable";
/// a returned location is a real recorded fix, never the shift-start guess.</summary>
public sealed record CustomerDeliveryLocationResult(
    double? Latitude,
    double? Longitude,
    DateTime? CapturedAtUtc,
    DateTime? ReceivedAtUtc,
    bool IsLive,
    string? LocationUnavailableReason,
    int? EtaMinutes,
    string? EtaSource);

public interface ICustomerDeliveryLocationBuilder
{
    Task<CustomerDeliveryLocationResult> BuildAsync(
        CustomerDeliveryLocationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resolves the freshest valid tracking fix of the box's currently assigned driver for
/// the current trip, classifies its freshness against the shared navigation policy, and
/// produces an ETA only from real evidence: 0 on confirmed arrival, or a successful real
/// routing result that respects this stop's position in the remaining ordered stops.
/// A shift-start coordinate is never reported as a live location and never produces an ETA.
/// Read-only: no business records are written.
/// </summary>
public sealed class CustomerDeliveryLocationBuilder : ICustomerDeliveryLocationBuilder
{
    /// <summary>A recorded time more than this far ahead of the server clock is a broken fix.</summary>
    private const int FutureToleranceSeconds = 30;

    public const string ReasonDriverMissing = "driver_location_missing";
    public const string ReasonLocationInvalid = "driver_location_invalid";
    public const string ReasonLocationStale = "driver_location_stale";
    public const string ReasonDestinationMissing = "destination_location_missing";
    public const string ReasonDirectionsUnavailable = "directions_unavailable";
    public const string ReasonEarlierStopsPending = "earlier_stops_pending";

    private readonly IAppDbContext _db;
    private readonly IGoogleRoutingService _routing;
    private readonly IMemoryCache _cache;
    private readonly IDateTimeProvider _clock;
    private readonly DriverNavigationOptions _options;

    public CustomerDeliveryLocationBuilder(
        IAppDbContext db,
        IGoogleRoutingService routing,
        IMemoryCache cache,
        IDateTimeProvider clock,
        IOptions<DriverNavigationOptions> options)
    {
        _db = db;
        _routing = routing;
        _cache = cache;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<CustomerDeliveryLocationResult> BuildAsync(
        CustomerDeliveryLocationRequest request,
        CancellationToken cancellationToken)
    {
        // Terminal boxes expose no current driver coordinates and no ETA —
        // the driver's live position is not the customer's business anymore.
        if (request.IsTerminal)
        {
            return new CustomerDeliveryLocationResult(
                null, null, null, null, false, null, null, null);
        }

        var tracking = await _db.DeliveryTrackingPoints.AsNoTracking()
            .Where(t => t.DriverProfileId == request.DriverProfileId && t.TripId == request.TripId)
            .OrderByDescending(t => t.RecordedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        (double? lat, double? lon, DateTime? capturedAt, DateTime? receivedAt, bool isLive, string? reason) = EvaluateTracking(tracking);

        // Arrival at the stop is real evidence: the driver is here, ETA is zero.
        if (request.IsArrived)
        {
            return new CustomerDeliveryLocationResult(
                lat, lon, capturedAt, receivedAt, isLive, reason,
                EtaMinutes: 0,
                EtaSource: CustomerDeliveryEtaSources.Arrival);
        }

        var eta = await ResolveRoadEtaAsync(request, lat, lon, isLive, cancellationToken);
        return new CustomerDeliveryLocationResult(
            lat, lon, capturedAt, receivedAt, isLive, reason,
            eta?.Minutes, eta?.Source);
    }

    private (double? Latitude, double? Longitude, DateTime? CapturedAtUtc, DateTime? ReceivedAtUtc, bool IsLive, string? Reason)
        EvaluateTracking(DeliveryTrackingPoint? tracking)
    {
        if (tracking is null)
        {
            // No fix exists for this driver+trip: nothing is claimed. The shift-start
            // session coordinate is deliberately NOT used as a live fallback.
            return (null, null, null, null, false, ReasonDriverMissing);
        }

        if (!TryGetValidPoint(tracking.Latitude, tracking.Longitude, out var latitude, out var longitude)
            || tracking.RecordedAtUtc > _clock.UtcNow.AddSeconds(FutureToleranceSeconds))
        {
            return (null, null, null, null, false, ReasonLocationInvalid);
        }

        var isStale = tracking.RecordedAtUtc <= _clock.UtcNow.AddSeconds(-_options.MaxLocationAgeSeconds);
        var captured = tracking.CapturedAtUtc ?? tracking.RecordedAtUtc;

        // A stale fix may remain visible as explicitly stale — never as live.
        return (latitude, longitude, captured, tracking.ReceivedAtUtc ?? tracking.RecordedAtUtc, !isStale,
            isStale ? ReasonLocationStale : null);
    }

    private async Task<(int Minutes, string Source)?> ResolveRoadEtaAsync(
        CustomerDeliveryLocationRequest request,
        double? driverLatitude,
        double? driverLongitude,
        bool isLive,
        CancellationToken cancellationToken)
    {
        if (!request.DestinationLatitude.HasValue
            || !request.DestinationLongitude.HasValue
            || !TryGetValidPoint(request.DestinationLatitude.Value, request.DestinationLongitude.Value, out var destinationLat, out var destinationLon))
        {
            return null;
        }

        // The driver's position in the remaining ordered stops decides whether a direct
        // route is a delivery ETA at all: with earlier undelivered stops the direct
        // duration would overstate arrival for this customer, so none is advertised.
        if (request.HasEarlierUndeliveredStops)
        {
            return null;
        }

        if (driverLatitude is null || driverLongitude is null || !isLive)
        {
            return null;
        }

        var cacheKey = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"customer-widget:route:{request.DriverProfileId:N}|{request.TripStopId:N}|{driverLatitude.Value:G17},{driverLongitude.Value:G17}");

        if (_cache.TryGetValue(cacheKey, out RouteEta? cached) && cached is not null)
        {
            return (cached.Minutes, CustomerDeliveryEtaSources.RoadRoute);
        }

        RouteDirectionsResult? directions;
        try
        {
            var result = await _routing.GetDirectionsAsync(
                (decimal)driverLatitude.Value, (decimal)driverLongitude.Value,
                (decimal)destinationLat, (decimal)destinationLon,
                cancellationToken);
            directions = result is { IsSuccess: true } ? result.Value : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Provider crash: fail closed — no ETA is invented from straight-line distance.
            return null;
        }

        if (directions is null
            || directions.IsFallbackEstimate
            || directions.DurationSeconds < 0)
        {
            return null;
        }

        var minutes = Math.Max(0, (int)Math.Ceiling(directions.DurationSeconds / 60.0));
        _cache.Set(cacheKey, new RouteEta(minutes), TimeSpan.FromSeconds(_options.RouteCacheSeconds));
        return (minutes, CustomerDeliveryEtaSources.RoadRoute);
    }

    private static bool TryGetValidPoint(decimal latitude, decimal longitude, out double validLatitude, out double validLongitude)
    {
        validLatitude = validLongitude = 0d;
        var lat = (double)latitude;
        var lon = (double)longitude;

        if (!double.IsFinite(lat) || !double.IsFinite(lon)
            || lat < -90d || lat > 90d || lon < -180d || lon > 180d)
        {
            return false;
        }

        if (!KuwaitGeoBounds.IsWithin(latitude, longitude))
        {
            return false;
        }

        validLatitude = lat;
        validLongitude = lon;
        return true;
    }

    private sealed record RouteEta(int Minutes);
}
