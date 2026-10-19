namespace MealMate.Application.Features.DriverDelivery.Trips;

public sealed record LocalizedTextDto(string Ar, string En);

public sealed record GeoPointDto(double Latitude, double Longitude);

public sealed record TripSummaryDto(
    string Id,
    LocalizedTextDto Label,
    int Value);

/// <summary>
/// Client stop status codes. Existing codes are stable; "returned_to_kitchen"
/// is an additive code for the pre-existing ReturnedToKitchen enum value that
/// previously collapsed into "pending".
/// </summary>
public static class TripStopClientStatus
{
    public const string Pending = "pending";
    public const string PickedUp = "picked_up";
    public const string Delivered = "delivered";
    public const string Failed = "failed";
    public const string ReturnedToKitchen = "returned_to_kitchen";
}

public sealed record TripStopDto(
    string Id,
    string OrderCode,
    string CustomerMaskedId,
    LocalizedTextDto ZoneLabel,
    LocalizedTextDto MealSummary,
    LocalizedTextDto? Address,
    GeoPointDto? Coordinates,
    int BoxCount,
    LocalizedTextDto SlotLabel,
    string Status,
    DateTime? EtaUtc,
    int? DistanceMeters,
    DateTime? ArrivedAtUtc,
    DateTime? PickedUpAtUtc,
    DateTime? DeliveredAtUtc,
    LocalizedTextDto? FailureReason);

/// <summary>
/// Route snapshot of one trip. Status is a semantic code — never display text:
/// "ready", "pending", "unavailable", "completed" or "stale_driver_location".
/// A missing or fallback polyline is null; consumers must never draw a
/// straight-line substitute.
/// </summary>
public sealed record TripRouteDto(
    string Status,
    GeoPointDto? Origin,
    GeoPointDto? Destination,
    GeoPointDto? DriverLocation,
    string? EncodedPolyline,
    string PolylineEncoding,
    int? DistanceMeters,
    int? DurationSeconds,
    DateTime? LastLocationAtUtc,
    string? UnavailableReason);

/// <summary>
/// One ordered trip event derived only from persisted lifecycle evidence.
/// Type is a semantic code; Note carries domain data, never UI copy.
/// </summary>
public sealed record TripTimelineEventDto(
    string Id,
    string Type,
    DateTime OccurredAtUtc,
    string? ActorName,
    string? StopId,
    LocalizedTextDto? Note);

public sealed record TripDriverOptionDto(
    string Id,
    LocalizedTextDto Name,
    string Code,
    LocalizedTextDto VehicleLabel,
    int TripsToday,
    bool Available);

public sealed record TripCandidateStopDto(
    string Id,
    string? OrderCode,
    string? CustomerMaskedId,
    LocalizedTextDto? ZoneLabel,
    LocalizedTextDto? MealSummary,
    int BoxCount,
    LocalizedTextDto? SlotLabel,
    LocalizedTextDto? ShiftLabel,
    LocalizedTextDto? Address,
    GeoPointDto? Coordinates);

public sealed record DeliveryTripDto(
    string Id,
    string Code,
    string Status,
    LocalizedTextDto ShiftLabel,
    LocalizedTextDto ZoneLabel,
    string? DriverId,
    LocalizedTextDto? DriverName,
    string? DriverCode,
    string? DispatcherId,
    LocalizedTextDto? DispatcherName,
    IReadOnlyList<TripStopDto> Stops,
    int BoxCount,
    int CustomerCount,
    LocalizedTextDto CreatedAtLabel,
    LocalizedTextDto? StartedAtLabel,
    LocalizedTextDto? CompletedAtLabel,
    LocalizedTextDto? Note,
    TripRouteDto? Route = null,
    IReadOnlyList<TripTimelineEventDto>? Timeline = null);

public sealed record RestaurantTripsDataDto(
    LocalizedTextDto Title,
    LocalizedTextDto Subtitle,
    LocalizedTextDto DateLabel,
    LocalizedTextDto Note,
    IReadOnlyList<TripSummaryDto> Summaries,
    IReadOnlyList<DeliveryTripDto> Trips,
    IReadOnlyList<TripCandidateStopDto> Candidates);
