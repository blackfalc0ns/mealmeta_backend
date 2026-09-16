namespace MealMate.Application.Features.DriverDelivery.Trips;

public sealed record LocalizedTextDto(string Ar, string En);

public sealed record TripSummaryDto(
    string Id,
    LocalizedTextDto Label,
    int Value);

public sealed record TripStopDto(
    string Id,
    string OrderCode,
    string CustomerMaskedId,
    LocalizedTextDto ZoneLabel,
    LocalizedTextDto MealSummary,
    int BoxCount,
    LocalizedTextDto SlotLabel,
    string Status);

public sealed record TripDriverOptionDto(
    string Id,
    LocalizedTextDto Name,
    string Code,
    LocalizedTextDto VehicleLabel,
    int TripsToday,
    bool Available);

public sealed record TripCandidateStopDto(
    string Id,
    string OrderCode,
    string CustomerMaskedId,
    LocalizedTextDto ZoneLabel,
    LocalizedTextDto MealSummary,
    int BoxCount,
    LocalizedTextDto SlotLabel,
    LocalizedTextDto ShiftLabel);

public sealed record DeliveryTripDto(
    string Id,
    string Code,
    string Status,
    LocalizedTextDto ShiftLabel,
    LocalizedTextDto ZoneLabel,
    string? DriverId,
    LocalizedTextDto? DriverName,
    string? DriverCode,
    string DispatcherId,
    LocalizedTextDto DispatcherName,
    IReadOnlyList<TripStopDto> Stops,
    int BoxCount,
    int CustomerCount,
    LocalizedTextDto CreatedAtLabel,
    LocalizedTextDto? StartedAtLabel,
    LocalizedTextDto? CompletedAtLabel,
    LocalizedTextDto? Note);

public sealed record RestaurantTripsDataDto(
    LocalizedTextDto Title,
    LocalizedTextDto Subtitle,
    LocalizedTextDto DateLabel,
    LocalizedTextDto Note,
    IReadOnlyList<TripSummaryDto> Summaries,
    IReadOnlyList<DeliveryTripDto> Trips,
    IReadOnlyList<TripCandidateStopDto> Candidates);
