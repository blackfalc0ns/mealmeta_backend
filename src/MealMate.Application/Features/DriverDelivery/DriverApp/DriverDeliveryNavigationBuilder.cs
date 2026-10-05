using System.Globalization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Domain.DriverDelivery;
using Microsoft.Extensions.Options;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

/// <summary>
/// Builds the navigation payload of the driver delivery-map screen.
/// Read-only over the data the map query already resolved: it never touches
/// the database and never stores tracking data — it only classifies location
/// freshness/validity and produces the localized pins and navigation link.
/// </summary>
public interface IDriverDeliveryNavigationBuilder
{
    Task<DriverNavigationDto> BuildAsync(
        Guid driverId,
        DriverMapStopDto focusedStop,
        DeliveryTrackingPoint? tracking,
        DriverWorkSession? session,
        bool isArabic,
        CancellationToken cancellationToken);
}

public sealed class DriverDeliveryNavigationBuilder : IDriverDeliveryNavigationBuilder
{
    public const string StatusReady = "Ready";
    public const string StatusUnavailable = "Unavailable";
    public const string StatusCompleted = "Completed";

    public const string ReasonLocationMissing = "driver_location_missing";
    public const string ReasonLocationStale = "driver_location_stale";
    public const string ReasonLocationInvalid = "driver_location_invalid";
    public const string ReasonDestinationMissing = "destination_location_missing";
    public const string ReasonDirectionsUnavailable = "directions_unavailable";

    public const string SourceTracking = "tracking";
    public const string SourceShiftStart = "shift_start";

    public const string PolylineEncodingGoogle5 = "google_polyline5";

    /// <summary>A recorded time more than this far ahead of the server clock is a broken fix, not a location.</summary>
    private const int FutureToleranceSeconds = 30;

    private readonly IDateTimeProvider _clock;
    private readonly DriverNavigationOptions _options;

    public DriverDeliveryNavigationBuilder(
        IDateTimeProvider clock,
        IOptions<DriverNavigationOptions> options)
    {
        _clock = clock;
        _options = options.Value;
    }

    public async Task<DriverNavigationDto> BuildAsync(
        Guid driverId,
        DriverMapStopDto focusedStop,
        DeliveryTrackingPoint? tracking,
        DriverWorkSession? session,
        bool isArabic,
        CancellationToken cancellationToken)
    {
        // Priority 1: a delivered stop never gets new directions — the card is
        // shown, but no route, no metrics, no navigation link.
        if (string.Equals(focusedStop.Status, "Delivered", StringComparison.OrdinalIgnoreCase))
        {
            return new DriverNavigationDto(
                DestinationStopId: focusedStop.StopId,
                Origin: EvaluateOrigin(tracking, session, isArabic).Point,
                Destination: BuildDestination(focusedStop, isArabic),
                RouteStatus: StatusCompleted,
                UnavailableReason: null,
                EncodedPolyline: null,
                PolylineEncoding: PolylineEncodingGoogle5,
                DistanceMeters: null,
                DurationSeconds: null,
                EstimatedArrivalAtUtc: null,
                CalculatedAtUtc: null,
                GoogleMapsUrl: null,
                CanNavigate: false);
        }

        // Priority 2: the destination must be a drawable, in-bounds pin.
        var destination = BuildDestination(focusedStop, isArabic);
        var canNavigate = destination is not null;

        // Priority 3 + 4: origin validity, then freshness.
        var (origin, originReason) = EvaluateOrigin(tracking, session, isArabic);

        if (destination is null)
        {
            // Destination outranks the origin in the status table: the card and
            // address stay available, but no route/navigation is possible.
            return Unavailable(focusedStop.StopId, origin, null, ReasonDestinationMissing, canNavigate: false);
        }

        if (originReason is not null)
        {
            return Unavailable(focusedStop.StopId, origin, destination, originReason, canNavigate);
        }

        // Priority 5: road geometry — a fresh location and valid destination alone
        // are never presented as a street route.
        var route = await GetDirectionsAsync(driverId, origin!, destination, cancellationToken);
        if (route is null)
        {
            return Unavailable(focusedStop.StopId, origin, destination, ReasonDirectionsUnavailable, canNavigate);
        }

        var calculatedAt = route.CalculatedAtUtc;
        return new DriverNavigationDto(
            DestinationStopId: focusedStop.StopId,
            Origin: origin,
            Destination: destination,
            RouteStatus: StatusReady,
            UnavailableReason: null,
            EncodedPolyline: route.EncodedPolyline,
            PolylineEncoding: PolylineEncodingGoogle5,
            DistanceMeters: route.DistanceMeters,
            DurationSeconds: route.DurationSeconds,
            EstimatedArrivalAtUtc: calculatedAt.AddSeconds(route.DurationSeconds),
            CalculatedAtUtc: calculatedAt,
            GoogleMapsUrl: BuildGoogleMapsUrl(destination),
            CanNavigate: canNavigate);
    }

    private static DriverNavigationDto Unavailable(
        Guid stopId,
        DriverNavigationPointDto? origin,
        DriverNavigationPointDto? destination,
        string reason,
        bool canNavigate)
    {
        return new DriverNavigationDto(
            DestinationStopId: stopId,
            Origin: origin,
            Destination: destination,
            RouteStatus: StatusUnavailable,
            UnavailableReason: reason,
            EncodedPolyline: null,
            PolylineEncoding: PolylineEncodingGoogle5,
            DistanceMeters: null,
            DurationSeconds: null,
            EstimatedArrivalAtUtc: null,
            CalculatedAtUtc: null,
            GoogleMapsUrl: canNavigate && destination is not null ? BuildGoogleMapsUrl(destination) : null,
            CanNavigate: canNavigate);
    }

    private static DriverNavigationPointDto? BuildDestination(DriverMapStopDto focusedStop, bool isArabic)
    {
        if (!TryGetValidPoint(focusedStop.Latitude, focusedStop.Longitude, out var latitude, out var longitude))
        {
            return null;
        }

        var label = string.IsNullOrWhiteSpace(focusedStop.CustomerName)
            ? (isArabic ? "وجهة التوصيل" : "Delivery destination")
            : focusedStop.CustomerName;

        return new DriverNavigationPointDto(
            Latitude: latitude,
            Longitude: longitude,
            Label: label,
            Source: "trip_stop",
            RecordedAtUtc: null,
            IsStale: false,
            Heading: null);
    }

    private (DriverNavigationPointDto? Point, string? Reason) EvaluateOrigin(
        DeliveryTrackingPoint? tracking,
        DriverWorkSession? session,
        bool isArabic)
    {
        if (tracking is not null)
        {
            // A recorded fix that is out of bounds or from the future is broken —
            // it is never drawn and never falls back to the session start.
            if (!TryGetValidPoint((double)tracking.Latitude, (double)tracking.Longitude, out var latitude, out var longitude)
                || tracking.RecordedAtUtc > _clock.UtcNow.AddSeconds(FutureToleranceSeconds))
            {
                return (null, ReasonLocationInvalid);
            }

            var isStale = tracking.RecordedAtUtc <= _clock.UtcNow.AddSeconds(-_options.MaxLocationAgeSeconds);
            var point = new DriverNavigationPointDto(
                Latitude: latitude,
                Longitude: longitude,
                Label: isArabic ? "موقعك الحالي" : "Your location",
                Source: SourceTracking,
                RecordedAtUtc: tracking.RecordedAtUtc,
                IsStale: isStale,
                Heading: tracking.Heading);

            return isStale ? (point, ReasonLocationStale) : (point, null);
        }

        // No tracking at all: the session start is the last thing we know, but it
        // is never a live location — even for a brand-new session.
        if (session is not null
            && session.StartLatitude.HasValue && session.StartLongitude.HasValue
            && TryGetValidPoint(session.StartLatitude, session.StartLongitude, out var startLat, out var startLon))
        {
            var point = new DriverNavigationPointDto(
                Latitude: startLat,
                Longitude: startLon,
                Label: isArabic ? "موقعك الحالي" : "Your location",
                Source: SourceShiftStart,
                RecordedAtUtc: null,
                IsStale: true,
                Heading: null);

            return (point, ReasonLocationStale);
        }

        return (null, ReasonLocationMissing);
    }

    private static bool TryGetValidPoint(double? latitude, double? longitude, out double validLatitude, out double validLongitude)
    {
        validLatitude = validLongitude = 0d;
        if (latitude is not { } lat || longitude is not { } lon)
        {
            return false;
        }

        // Pre-filter to sane geographic ranges so the decimal conversion for the
        // Kuwait bounds check can never overflow on garbage input.
        if (!double.IsFinite(lat) || !double.IsFinite(lon)
            || lat < -90d || lat > 90d || lon < -180d || lon > 180d)
        {
            return false;
        }

        if (!KuwaitGeoBounds.IsWithin((decimal)lat, (decimal)lon))
        {
            return false;
        }

        validLatitude = lat;
        validLongitude = lon;
        return true;
    }

    private static string BuildGoogleMapsUrl(DriverNavigationPointDto destination)
    {
        // Deep link to the destination only — no stale origin is pinned; the
        // maps app picks up the device position itself.
        var lat = destination.Latitude.ToString(CultureInfo.InvariantCulture);
        var lon = destination.Longitude.ToString(CultureInfo.InvariantCulture);
        var target = Uri.EscapeDataString($"{lat},{lon}");
        return $"https://www.google.com/maps/dir/?api=1&destination={target}&travelmode=driving&dir_action=navigate";
    }

    /// <summary>
    /// Returns the real street route for the given pins, or null when no
    /// trustworthy directions exist. A valid-looking location pair must never be
    /// presented as street geometry without a provider answer.
    /// </summary>
    private Task<RealRoute?> GetDirectionsAsync(
        Guid driverId,
        DriverNavigationPointDto origin,
        DriverNavigationPointDto destination,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<RealRoute?>(null);
    }

    private sealed record RealRoute(string EncodedPolyline, long DistanceMeters, long DurationSeconds, DateTime CalculatedAtUtc);
}
