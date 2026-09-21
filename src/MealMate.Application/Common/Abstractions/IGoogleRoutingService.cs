using MealMate.Application.Common.Models;

namespace MealMate.Application.Common.Abstractions;

public interface IGoogleRoutingService
{
    Task<Result<RouteDistanceDurationResult>> CalculateDistanceAndDurationAsync(
        decimal originLatitude,
        decimal originLongitude,
        decimal destinationLatitude,
        decimal destinationLongitude,
        CancellationToken cancellationToken = default);

    Task<Result<RouteDirectionsResult>> GetDirectionsAsync(
        decimal originLatitude,
        decimal originLongitude,
        decimal destinationLatitude,
        decimal destinationLongitude,
        CancellationToken cancellationToken = default);
}

public sealed record RouteDistanceDurationResult(
    long DistanceMeters,
    double DistanceKm,
    long DurationSeconds,
    int DurationMinutes,
    string DistanceText,
    string DurationText,
    bool IsFallbackEstimate);

public sealed record RouteDirectionsResult(
    long DistanceMeters,
    double DistanceKm,
    long DurationSeconds,
    int DurationMinutes,
    string? OverviewPolyline,
    bool IsFallbackEstimate,
    decimal OriginLatitude,
    decimal OriginLongitude,
    decimal DestinationLatitude,
    decimal DestinationLongitude);
