namespace MealMate.Application.Features.DriverDelivery.DriverApp;

/// <summary>
/// One map pin of the driver navigation payload. <see cref="Source"/> is
/// "tracking" for a live delivery-tracking fix or "shift_start" for the work
/// session's start coordinates (never treated as a live location).
/// </summary>
public sealed record DriverNavigationPointDto(
    double Latitude,
    double Longitude,
    string Label,
    string Source,
    DateTime? RecordedAtUtc,
    bool IsStale,
    double? Heading);

/// <summary>
/// Navigation payload for the driver delivery-map screen (03.01): current
/// driver position, the focused customer stop, and — only when the real
/// routing provider returned road geometry — the encoded street route with
/// distance/duration and an external Google Maps navigation link.
/// Distances are meters, durations seconds; the polyline is encoded with
/// Google's polyline5 algorithm (<see cref="PolylineEncoding"/>).
/// </summary>
public sealed record DriverNavigationDto(
    Guid? DestinationStopId,
    DriverNavigationPointDto? Origin,
    DriverNavigationPointDto? Destination,
    string RouteStatus,
    string? UnavailableReason,
    string? EncodedPolyline,
    string PolylineEncoding,
    long? DistanceMeters,
    long? DurationSeconds,
    DateTime? EstimatedArrivalAtUtc,
    DateTime? CalculatedAtUtc,
    string? GoogleMapsUrl,
    bool CanNavigate);
