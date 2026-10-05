using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Application.Common.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MealMate.Infrastructure.Services;

public sealed class GoogleRoutingService : IGoogleRoutingService
{
    private readonly HttpClient _http;
    private readonly GoogleMapsOptions _options;
    private readonly ILogger<GoogleRoutingService> _logger;

    public GoogleRoutingService(
        HttpClient http,
        IOptions<GoogleMapsOptions> options,
        ILogger<GoogleRoutingService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<RouteDistanceDurationResult>> CalculateDistanceAndDurationAsync(
        decimal originLatitude,
        decimal originLongitude,
        decimal destinationLatitude,
        decimal destinationLongitude,
        CancellationToken cancellationToken = default)
    {
        var apiKey = ResolveApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var origin = $"{originLatitude.ToString(CultureInfo.InvariantCulture)},{originLongitude.ToString(CultureInfo.InvariantCulture)}";
                var destination = $"{destinationLatitude.ToString(CultureInfo.InvariantCulture)},{destinationLongitude.ToString(CultureInfo.InvariantCulture)}";
                var url = $"{_options.DistanceMatrixBaseUrl.TrimEnd('/')}?origins={origin}&destinations={destination}&mode=driving&language=ar&key={Uri.EscapeDataString(apiKey)}";

                using var response = await _http.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var payload = await response.Content.ReadFromJsonAsync<GoogleDistanceMatrixResponse>(cancellationToken);
                    if (payload is not null && string.Equals(payload.Status, "OK", StringComparison.OrdinalIgnoreCase))
                    {
                        var element = payload.Rows?.FirstOrDefault()?.Elements?.FirstOrDefault();
                        if (element is not null && string.Equals(element.Status, "OK", StringComparison.OrdinalIgnoreCase))
                        {
                            var distMeters = element.Distance?.Value ?? 0;
                            var durSecs = element.Duration?.Value ?? 0;
                            var distKm = Math.Round((double)distMeters / 1000.0, 2);
                            var durMins = (int)Math.Max(1, Math.Ceiling((double)durSecs / 60.0));

                            return Result.Success(new RouteDistanceDurationResult(
                                DistanceMeters: distMeters,
                                DistanceKm: distKm,
                                DurationSeconds: durSecs,
                                DurationMinutes: durMins,
                                DistanceText: element.Distance?.Text ?? $"{distKm:F1} كم",
                                DurationText: element.Duration?.Text ?? $"{durMins} دقيقة",
                                IsFallbackEstimate: false));
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Google Distance Matrix API call failed, falling back to Haversine calculation.");
            }
        }

        // Mathematical fallback using Kuwait Haversine formula
        return Result.Success(CalculateFallbackDistanceDuration(originLatitude, originLongitude, destinationLatitude, destinationLongitude));
    }

    public async Task<Result<RouteDirectionsResult>> GetDirectionsAsync(
        decimal originLatitude,
        decimal originLongitude,
        decimal destinationLatitude,
        decimal destinationLongitude,
        CancellationToken cancellationToken = default)
    {
        var apiKey = ResolveApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var origin = $"{originLatitude.ToString(CultureInfo.InvariantCulture)},{originLongitude.ToString(CultureInfo.InvariantCulture)}";
                var destination = $"{destinationLatitude.ToString(CultureInfo.InvariantCulture)},{destinationLongitude.ToString(CultureInfo.InvariantCulture)}";
                var url = $"{_options.DirectionsBaseUrl.TrimEnd('/')}?origin={origin}&destination={destination}&mode=driving&language=ar&key={Uri.EscapeDataString(apiKey)}";

                using var response = await _http.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var payload = await response.Content.ReadFromJsonAsync<GoogleDirectionsResponse>(cancellationToken);
                    if (payload is not null && string.Equals(payload.Status, "OK", StringComparison.OrdinalIgnoreCase))
                    {
                        var route = payload.Routes?.FirstOrDefault();
                        var leg = route?.Legs?.FirstOrDefault();

                        // Missing distance/duration in the Google response is a
                        // not-ready answer: it must fall back to the estimate, never
                        // become a zero-meters zero-seconds success.
                        if (leg is not null
                            && leg.Distance?.Value is { } distMeters
                            && leg.Duration?.Value is { } durSecs)
                        {
                            var distKm = Math.Round((double)distMeters / 1000.0, 2);
                            var durMins = (int)Math.Max(1, Math.Ceiling((double)durSecs / 60.0));
                            var polyline = route?.OverviewPolyline?.Points;

                            return Result.Success(new RouteDirectionsResult(
                                DistanceMeters: distMeters,
                                DistanceKm: distKm,
                                DurationSeconds: durSecs,
                                DurationMinutes: durMins,
                                OverviewPolyline: polyline,
                                IsFallbackEstimate: false,
                                OriginLatitude: originLatitude,
                                OriginLongitude: originLongitude,
                                DestinationLatitude: destinationLatitude,
                                DestinationLongitude: destinationLongitude));
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Google Directions API call failed, falling back to Haversine calculation.");
            }
        }

        var fallback = CalculateFallbackDistanceDuration(originLatitude, originLongitude, destinationLatitude, destinationLongitude);
        return Result.Success(new RouteDirectionsResult(
            DistanceMeters: fallback.DistanceMeters,
            DistanceKm: fallback.DistanceKm,
            DurationSeconds: fallback.DurationSeconds,
            DurationMinutes: fallback.DurationMinutes,
            OverviewPolyline: null,
            IsFallbackEstimate: true,
            OriginLatitude: originLatitude,
            OriginLongitude: originLongitude,
            DestinationLatitude: destinationLatitude,
            DestinationLongitude: destinationLongitude));
    }

    private string? ResolveApiKey()
    {
        var key = _options.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            key = Environment.GetEnvironmentVariable("GOOGLE_MAPS_SERVER_API_KEY");
        }
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    private static RouteDistanceDurationResult CalculateFallbackDistanceDuration(
        decimal originLat, decimal originLng, decimal destLat, decimal destLng)
    {
        var straightLineKm = KuwaitGeoBounds.HaversineKm(originLat, originLng, destLat, destLng);
        // City road winding factor ~ 1.3x straight-line distance
        var roadDistanceKm = Math.Round(straightLineKm * 1.3, 2);
        var distanceMeters = (long)Math.Round(roadDistanceKm * 1000.0);

        // Assume average Kuwait city delivery traffic speed: 38 km/h
        const double avgSpeedKmh = 38.0;
        var durationHours = roadDistanceKm / avgSpeedKmh;
        var durationSeconds = (long)Math.Max(60, Math.Round(durationHours * 3600.0));
        var durationMinutes = (int)Math.Max(1, Math.Ceiling((double)durationSeconds / 60.0));

        return new RouteDistanceDurationResult(
            DistanceMeters: distanceMeters,
            DistanceKm: roadDistanceKm,
            DurationSeconds: durationSeconds,
            DurationMinutes: durationMinutes,
            DistanceText: $"{roadDistanceKm:F1} كم",
            DurationText: $"{durationMinutes} دقيقة",
            IsFallbackEstimate: true);
    }
}

file sealed class GoogleDistanceMatrixResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("rows")]
    public List<GoogleDistanceMatrixRow>? Rows { get; set; }
}

file sealed class GoogleDistanceMatrixRow
{
    [JsonPropertyName("elements")]
    public List<GoogleDistanceMatrixElement>? Elements { get; set; }
}

file sealed class GoogleDistanceMatrixElement
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("distance")]
    public GoogleValueText? Distance { get; set; }

    [JsonPropertyName("duration")]
    public GoogleValueText? Duration { get; set; }
}

file sealed class GoogleDirectionsResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("routes")]
    public List<GoogleDirectionsRoute>? Routes { get; set; }
}

file sealed class GoogleDirectionsRoute
{
    [JsonPropertyName("overview_polyline")]
    public GoogleOverviewPolyline? OverviewPolyline { get; set; }

    [JsonPropertyName("legs")]
    public List<GoogleDirectionsLeg>? Legs { get; set; }
}

file sealed class GoogleOverviewPolyline
{
    [JsonPropertyName("points")]
    public string? Points { get; set; }
}

file sealed class GoogleDirectionsLeg
{
    [JsonPropertyName("distance")]
    public GoogleValueText? Distance { get; set; }

    [JsonPropertyName("duration")]
    public GoogleValueText? Duration { get; set; }
}

file sealed class GoogleValueText
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("value")]
    public long? Value { get; set; }
}
