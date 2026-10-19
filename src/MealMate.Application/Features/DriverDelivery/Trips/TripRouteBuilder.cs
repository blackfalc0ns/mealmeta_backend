using System.Globalization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MealMate.Application.Features.DriverDelivery.Trips;

/// <summary>
/// Builds the map route snapshot of a single trip from real persisted evidence.
/// Street geometry always comes from the routing provider; a missing, fallback
/// or malformed polyline is reported as null with an explicit status, and the
/// client must never draw a straight-line substitute.
/// </summary>
public interface ITripRouteBuilder
{
    Task<TripRouteResult> BuildAsync(TripRouteRequest request, CancellationToken cancellationToken);
}

/// <summary>Everything the trip detail query needs to decide the route snapshot.</summary>
public sealed record TripRouteRequest(
    Trip Trip,
    IReadOnlyList<TripStop> Stops,
    decimal? RestaurantLatitude,
    decimal? RestaurantLongitude,
    DeliveryTrackingPoint? DriverTracking,
    bool DriverLocationStale);

/// <summary>Route snapshot plus per-stop ETA/distance evidence for the remaining legs.</summary>
public sealed record TripRouteResult(
    TripRouteDto Route,
    IReadOnlyDictionary<Guid, StopRouteEvidence> StopEvidence);

/// <summary>Per-stop route evidence. Values stay null when no real provider answer exists.</summary>
public sealed record StopRouteEvidence(
    DateTime? EtaUtc,
    int? DistanceMeters);

public sealed class TripRouteBuilder : ITripRouteBuilder
{
    public const string StatusReady = "ready";
    public const string StatusPending = "pending";
    public const string StatusUnavailable = "unavailable";
    public const string StatusCompleted = "completed";
    public const string StatusStaleDriverLocation = "stale_driver_location";

    public const string ReasonRestaurantOriginMissing = "restaurant_origin_coordinates_missing";
    public const string ReasonStopCoordinatesMissing = "stop_coordinates_missing";
    public const string ReasonProviderGeometryMissing = "provider_geometry_missing";
    public const string ReasonRoutingProviderUnavailable = "routing_provider_unavailable";
    public const string ReasonTripCancelled = "trip_cancelled";

    public const string PolylineEncodingGoogle5 = "google_polyline5";

    /// <summary>One leg answer of the routing provider.</summary>
    private sealed record LegAnswer(TripStop? Target, string EncodedPolyline, long DistanceMeters, long DurationSeconds);

    /// <summary>A recorded fix ahead of the server clock is broken, not a location.</summary>
    private const int FutureToleranceSeconds = 30;
    private const int DefaultMaxLocationAgeSeconds = 120;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    private readonly IGoogleRoutingService _routing;
    private readonly IDateTimeProvider _clock;
    private readonly IMemoryCache _cache;
    private readonly int _maxLocationAgeSeconds;

    public TripRouteBuilder(
        IGoogleRoutingService routing,
        IDateTimeProvider clock,
        IMemoryCache cache,
        IOptions<DriverNavigationOptions> options)
    {
        _routing = routing;
        _clock = clock;
        _cache = cache;
        _maxLocationAgeSeconds = options.Value.MaxLocationAgeSeconds > 0
            ? options.Value.MaxLocationAgeSeconds
            : DefaultMaxLocationAgeSeconds;
    }

    public async Task<TripRouteResult> BuildAsync(TripRouteRequest request, CancellationToken cancellationToken)
    {
        var orderedStops = OrderStops(request.Stops);
        var origin = ValidPoint(request.RestaurantLatitude, request.RestaurantLongitude);

        // Terminal trips keep their delivered markers, but no route is calculated
        // and no driver position is presented as current.
        if (request.Trip.Status is TripStatus.Completed or TripStatus.Cancelled)
        {
            var reason = request.Trip.Status == TripStatus.Cancelled ? ReasonTripCancelled : null;
            return BuildTerminal(orderedStops, origin, reason);
        }

        var chainStops = orderedStops;
        if (request.Trip.Status == TripStatus.InProgress)
        {
            // Once the driver has departed, the live route continues from the
            // driver's last position through the remaining stops — but only a
            // fresh fix may anchor the route; a stale fix keeps the restaurant
            // as origin and the route is flagged stale_driver_location instead.
            var remaining = orderedStops
                .Where(s => s.Status is TripStopStatus.Pending or TripStopStatus.PickedUp)
                .ToArray();
            var driverPoint = ValidPoint(
                request.DriverTracking is null ? null : (decimal)request.DriverTracking.Latitude,
                request.DriverTracking is null ? null : (decimal)request.DriverTracking.Longitude);
            if (remaining.Length > 0
                && driverPoint is not null
                && request.DriverTracking is not null
                && IsFreshFix(request.DriverTracking.RecordedAtUtc, _clock.UtcNow))
            {
                chainStops = remaining;
                origin = driverPoint;
            }
        }

        var cacheKey = BuildCacheKey(request, chainStops, origin);
        if (_cache.TryGetValue(cacheKey, out TripRouteResult? cached) && cached is not null)
        {
            return cached;
        }

        var result = await CalculateAsync(request, orderedStops, chainStops, origin, cancellationToken);
        _cache.Set(cacheKey, result, CacheDuration);
        return result;
    }

    private async Task<TripRouteResult> CalculateAsync(
        TripRouteRequest request,
        IReadOnlyList<TripStop> orderedStops,
        IReadOnlyList<TripStop> chainStops,
        GeoPointDto? origin,
        CancellationToken cancellationToken)
    {
        if (origin is null)
        {
            return Unavailable(request, orderedStops, ReasonRestaurantOriginMissing);
        }

        if (chainStops.Count == 0 || chainStops.Any(s => ValidPoint(s.Latitude, s.Longitude) is null))
        {
            return Unavailable(request, orderedStops, ReasonStopCoordinatesMissing);
        }

        var chain = new List<(TripStop? Stop, GeoPointDto Point)>(chainStops.Count + 1) { (null, origin) };
        chain.AddRange(chainStops.Select(s => (Stop: (TripStop?)s, Point: ValidPoint(s.Latitude, s.Longitude)!)));

        var now = _clock.UtcNow;
        var legs = new List<LegAnswer>();
        long totalDistance = 0;
        long totalDuration = 0;
        var allReal = true;

        for (var i = 0; i < chain.Count - 1; i++)
        {
            var from = chain[i].Point;
            var to = chain[i + 1].Point;
            RouteDirectionsResult? directions = null;
            try
            {
                var call = await _routing.GetDirectionsAsync(
                    (decimal)from.Latitude, (decimal)from.Longitude,
                    (decimal)to.Latitude, (decimal)to.Longitude,
                    cancellationToken);
                if (call is { IsSuccess: true }) directions = call.Value;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                directions = null;
            }

            if (directions is null || directions.IsFallbackEstimate || string.IsNullOrWhiteSpace(directions.OverviewPolyline))
            {
                allReal = false;
                continue;
            }

            totalDistance += directions.DistanceMeters;
            totalDuration += directions.DurationSeconds;
            legs.Add(new LegAnswer(chain[i + 1].Stop, directions.OverviewPolyline, directions.DistanceMeters, directions.DurationSeconds));
        }

        if (!allReal || legs.Count == 0)
        {
            return Unavailable(request, orderedStops, ReasonProviderGeometryMissing);
        }

        var decoded = ConcatenatePolyline(legs.Select(l => l.EncodedPolyline));
        var encoded = decoded.Count >= 2 ? EncodePolyline(decoded) : null;
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return Unavailable(request, orderedStops, ReasonProviderGeometryMissing);
        }

        // Per-stop ETA accumulates along the drawn chain; stops outside the chain
        // (delivered, failed, skipped legs) keep null evidence — never a fabricated estimate.
        var evidence = new Dictionary<Guid, StopRouteEvidence>();
        long cumulativeDuration = 0;
        long cumulativeDistance = 0;
        foreach (var leg in legs)
        {
            if (leg.Target is null) continue;
            cumulativeDuration += leg.DurationSeconds;
            cumulativeDistance += leg.DistanceMeters;
            evidence[leg.Target.Id] = new StopRouteEvidence(
                now.AddSeconds(cumulativeDuration),
                (int)cumulativeDistance);
        }

        var driver = DriverLocationOf(request, now);
        var status = driver.IsStale ? StatusStaleDriverLocation : StatusReady;
        var route = new TripRouteDto(
            status,
            origin,
            chain[^1].Point,
            driver.Point,
            encoded,
            PolylineEncodingGoogle5,
            (int)totalDistance,
            (int)totalDuration,
            driver.Point is null ? null : driver.RecordedAtUtc,
            UnavailableReason: null);

        var merged = orderedStops.ToDictionary(
            s => s.Id,
            s => evidence.TryGetValue(s.Id, out var e) ? e : new StopRouteEvidence(null, null));
        return new TripRouteResult(route, merged);
    }

    private static TripRouteResult BuildTerminal(
        IReadOnlyList<TripStop> orderedStops,
        GeoPointDto? origin,
        string? reason)
    {
        var deliveredStops = orderedStops.Where(s => s.Status == TripStopStatus.Delivered).ToArray();
        var destination = deliveredStops.Length > 0
            ? ValidPoint(deliveredStops[^1].Latitude, deliveredStops[^1].Longitude)
            : null;
        var status = reason is null ? StatusCompleted : StatusUnavailable;
        return new TripRouteResult(
            new TripRouteDto(
                status,
                origin,
                destination,
                DriverLocation: null,
                EncodedPolyline: null,
                PolylineEncoding: PolylineEncodingGoogle5,
                DistanceMeters: null,
                DurationSeconds: null,
                LastLocationAtUtc: null,
                UnavailableReason: reason),
            orderedStops.ToDictionary(s => s.Id, _ => new StopRouteEvidence(null, null)));
    }

    private TripRouteResult Unavailable(
        TripRouteRequest request,
        IReadOnlyList<TripStop> orderedStops,
        string reason)
    {
        var deliveredStops = orderedStops.Where(s => s.Status == TripStopStatus.Delivered).ToArray();
        var destination = deliveredStops.Length > 0
            ? ValidPoint(deliveredStops[^1].Latitude, deliveredStops[^1].Longitude)
            : null;
        var driver = DriverLocationOf(request, _clock.UtcNow);
        return new TripRouteResult(
            new TripRouteDto(
                StatusUnavailable,
                ValidPoint(request.RestaurantLatitude, request.RestaurantLongitude),
                destination,
                driver.Point,
                EncodedPolyline: null,
                PolylineEncoding: PolylineEncodingGoogle5,
                DistanceMeters: null,
                DurationSeconds: null,
                driver.Point is null ? null : driver.RecordedAtUtc,
                reason),
            orderedStops.ToDictionary(s => s.Id, _ => new StopRouteEvidence(null, null)));
    }

    private (GeoPointDto? Point, DateTime? RecordedAtUtc, bool IsStale) DriverLocationOf(
        TripRouteRequest request, DateTime now)
    {
        var tracking = request.DriverTracking;
        if (tracking is null)
        {
            return (null, null, false);
        }

        var point = ValidPoint((decimal)tracking.Latitude, (decimal)tracking.Longitude);
        if (point is null)
        {
            return (null, tracking.RecordedAtUtc, true);
        }

        var isStale = request.DriverLocationStale || !IsFreshFix(tracking.RecordedAtUtc, now);

        return (point, tracking.RecordedAtUtc, isStale);
    }

    private bool IsFreshFix(DateTime recordedAtUtc, DateTime now)
        => recordedAtUtc > now.AddSeconds(-_maxLocationAgeSeconds)
            && recordedAtUtc <= now.AddSeconds(FutureToleranceSeconds);

    private static IReadOnlyList<TripStop> OrderStops(IReadOnlyList<TripStop> stops)
        => stops.Where(s => !s.IsDeleted).OrderBy(s => s.SequenceNumber).ThenBy(s => s.Id).ToArray();

    private static GeoPointDto? ValidPoint(decimal? latitude, decimal? longitude)
        => latitude is >= -90m and <= 90m && longitude is >= -180m and <= 180m
            ? new GeoPointDto((double)latitude.Value, (double)longitude.Value)
            : null;

    private static string BuildCacheKey(TripRouteRequest request, IReadOnlyList<TripStop> chainStops, GeoPointDto? origin)
    {
        var key = new System.Text.StringBuilder("trip-route:")
            .Append(request.Trip.Id.ToString("N"))
            .Append(':').Append((int)request.Trip.Status)
            .Append(':').Append(origin?.Latitude.ToString("G17", CultureInfo.InvariantCulture) ?? "-")
            .Append(',').Append(origin?.Longitude.ToString("G17", CultureInfo.InvariantCulture) ?? "-")
            .Append(':').Append(request.DriverLocationStale ? "stale" : "live");
        foreach (var stop in chainStops)
        {
            key.Append('|').Append(stop.SequenceNumber).Append(':').Append(stop.Id.ToString("N"))
                .Append(':').Append((int)stop.Status)
                .Append(':').Append(stop.Latitude?.ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(stop.Longitude?.ToString(CultureInfo.InvariantCulture));
        }

        return key.ToString();
    }

    /// <summary>Concatenates leg polylines into one path, dropping repeated join points. Returns an empty list for any malformed leg.</summary>
    public static List<GeoPointDto> ConcatenatePolyline(IEnumerable<string> encodedPolylines)
    {
        var path = new List<GeoPointDto>();
        foreach (var encoded in encodedPolylines)
        {
            var leg = DecodePolyline(encoded);
            if (leg.Count == 0) return [];
            if (path.Count > 0 && IsSamePoint(path[^1], leg[0]))
            {
                leg.RemoveAt(0);
            }

            path.AddRange(leg);
        }

        return path;
    }

    private static bool IsSamePoint(GeoPointDto a, GeoPointDto b)
        => Math.Abs(a.Latitude - b.Latitude) < 1e-6 && Math.Abs(a.Longitude - b.Longitude) < 1e-6;

    /// <summary>Decodes Google polyline5. Returns an empty list for any malformed input.</summary>
    public static List<GeoPointDto> DecodePolyline(string? encoded)
    {
        var points = new List<GeoPointDto>();
        if (string.IsNullOrWhiteSpace(encoded)) return points;

        var index = 0;
        double latitude = 0;
        double longitude = 0;
        while (index < encoded.Length)
        {
            if (!DecodeChunk(encoded, ref index, ref latitude)) return [];
            if (!DecodeChunk(encoded, ref index, ref longitude)) return [];
            if (latitude / 1e5 < -90d || latitude / 1e5 > 90d || longitude / 1e5 < -180d || longitude / 1e5 > 180d) return [];
            points.Add(new GeoPointDto(latitude / 1e5, longitude / 1e5));
        }

        return points;
    }

    private static bool DecodeChunk(string encoded, ref int index, ref double value)
    {
        long result = 0;
        var shift = 0;
        while (true)
        {
            if (index >= encoded.Length || shift > 30) return false;
            var byteValue = encoded[index++] - 63;
            if (byteValue < 0 || byteValue > 63) return false;
            result |= (byteValue & 0x1f) << shift;
            shift += 5;
            if (byteValue < 0x20) break;
        }

        value += (result & 1) != 0 ? ~(result >> 1) : (result >> 1);
        return true;
    }

    /// <summary>Encodes Google polyline5 from a validated path. Returns null when the path is not a drawable polyline.</summary>
    public static string? EncodePolyline(IReadOnlyList<GeoPointDto> path)
    {
        if (path.Count < 2) return null;
        var builder = new System.Text.StringBuilder();
        long previousLat = 0;
        long previousLng = 0;
        foreach (var point in path)
        {
            if (point.Latitude < -90d || point.Latitude > 90d || point.Longitude < -180d || point.Longitude > 180d)
            {
                return null;
            }

            var lat = (long)Math.Round(point.Latitude * 1e5);
            var lng = (long)Math.Round(point.Longitude * 1e5);
            AppendChunk(builder, lat - previousLat);
            AppendChunk(builder, lng - previousLng);
            previousLat = lat;
            previousLng = lng;
        }

        return builder.ToString();
    }

    private static void AppendChunk(System.Text.StringBuilder builder, long value)
    {
        var shifted = value < 0 ? ~(value << 1) : value << 1;
        while (shifted >= 0x20)
        {
            builder.Append((char)((0x20 | (int)(shifted & 0x1f)) + 63));
            shifted >>= 5;
        }

        builder.Append((char)((int)shifted + 63));
    }
}
