using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MealMate.Infrastructure.Services;

public sealed class GoogleGeocodingService : IGoogleGeocodingService
{
    private readonly HttpClient _http;
    private readonly GoogleMapsOptions _options;
    private readonly ILogger<GoogleGeocodingService> _logger;

    public GoogleGeocodingService(
        HttpClient http,
        IOptions<GoogleMapsOptions> options,
        ILogger<GoogleGeocodingService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public Task<Result<GoogleGeocodeResult>> GeocodeAddressAsync(
        string address,
        string countryCode = "KW",
        CancellationToken cancellationToken = default)
    {
        var trimmed = address?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Task.FromResult(Result.Failure<GoogleGeocodeResult>(
                Error.Validation(MessageCodes.Validation.RequiredField, "Address is required.")));
        }

        var query =
            $"?address={Uri.EscapeDataString(trimmed)}" +
            $"&components=country:{Uri.EscapeDataString(countryCode.Trim().ToUpperInvariant())}" +
            $"&language=ar";

        return ExecuteAsync(query, cancellationToken);
    }

    public Task<Result<GoogleGeocodeResult>> ReverseGeocodeAsync(
        decimal latitude,
        decimal longitude,
        CancellationToken cancellationToken = default)
    {
        var query =
            $"?latlng={latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            "&language=ar";

        return ExecuteAsync(query, cancellationToken);
    }

    private async Task<Result<GoogleGeocodeResult>> ExecuteAsync(string query, CancellationToken cancellationToken)
    {
        var apiKey = _options.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = Environment.GetEnvironmentVariable("GOOGLE_MAPS_SERVER_API_KEY");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Error.Validation(
                MessageCodes.Validation.InvalidFormat,
                "Google Maps server API key is not configured.");
        }

        try
        {
            var url = $"{_options.GeocodeBaseUrl.TrimEnd('/')}{query}&key={Uri.EscapeDataString(apiKey)}";
            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google Geocoding HTTP {StatusCode}", (int)response.StatusCode);
                return Error.Validation(MessageCodes.Validation.InvalidFormat, "Geocoding request failed.");
            }

            var payload = await response.Content.ReadFromJsonAsync<GoogleGeocodeResponse>(cancellationToken);
            if (payload is null || !string.Equals(payload.Status, "OK", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Google Geocoding status {Status}", payload?.Status);
                return Error.Validation(
                    MessageCodes.Validation.InvalidFormat,
                    "Could not resolve coordinates for the given address.");
            }

            var first = payload.Results?.FirstOrDefault();
            var location = first?.Geometry?.Location;
            if (first is null || location is null || string.IsNullOrWhiteSpace(first.FormattedAddress))
            {
                return Error.Validation(
                    MessageCodes.Validation.InvalidFormat,
                    "Could not resolve coordinates for the given address.");
            }

            var parts = ParseComponents(first.AddressComponents);
            return new GoogleGeocodeResult(
                first.FormattedAddress.Trim(),
                Convert.ToDecimal(location.Lat),
                Convert.ToDecimal(location.Lng),
                parts.Governorate,
                parts.Area,
                parts.Block,
                parts.Street,
                parts.Building);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Google Geocoding request error");
            return Error.Validation(MessageCodes.Validation.InvalidFormat, "Geocoding request failed.");
        }
    }

    private static (
        string? Governorate,
        string? Area,
        string? Block,
        string? Street,
        string? Building) ParseComponents(IReadOnlyList<GoogleAddressComponent>? components)
    {
        if (components is null || components.Count == 0)
        {
            return (null, null, null, null, null);
        }

        string? Get(params string[] types)
        {
            foreach (var type in types)
            {
                var match = components.FirstOrDefault(c =>
                    c.Types is not null
                    && c.Types.Any(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase)));
                if (!string.IsNullOrWhiteSpace(match?.LongName))
                {
                    return match!.LongName!.Trim();
                }
            }

            return null;
        }

        var governorate = Get("administrative_area_level_1");
        var area = Get("locality", "sublocality", "sublocality_level_1", "administrative_area_level_2", "neighborhood");
        var block = Get("sublocality_level_2", "neighborhood");
        if (string.Equals(block, area, StringComparison.OrdinalIgnoreCase))
        {
            block = null;
        }

        var route = Get("route");
        var streetNumber = Get("street_number");
        var premise = Get("premise", "establishment");
        var street = string.IsNullOrWhiteSpace(route)
            ? null
            : string.IsNullOrWhiteSpace(streetNumber) ? route : $"{route} {streetNumber}".Trim();
        var building = premise ?? (string.IsNullOrWhiteSpace(route) ? streetNumber : null);

        return (governorate, area, block, street, building);
    }

    private sealed class GoogleGeocodeResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("results")]
        public List<GoogleGeocodeApiResult>? Results { get; set; }
    }

    private sealed class GoogleGeocodeApiResult
    {
        [JsonPropertyName("formatted_address")]
        public string? FormattedAddress { get; set; }

        [JsonPropertyName("address_components")]
        public List<GoogleAddressComponent>? AddressComponents { get; set; }

        [JsonPropertyName("geometry")]
        public GoogleGeocodeGeometry? Geometry { get; set; }
    }

    private sealed class GoogleAddressComponent
    {
        [JsonPropertyName("long_name")]
        public string? LongName { get; set; }

        [JsonPropertyName("short_name")]
        public string? ShortName { get; set; }

        [JsonPropertyName("types")]
        public List<string>? Types { get; set; }
    }

    private sealed class GoogleGeocodeGeometry
    {
        [JsonPropertyName("location")]
        public GoogleGeocodeLatLng? Location { get; set; }
    }

    private sealed class GoogleGeocodeLatLng
    {
        [JsonPropertyName("lat")]
        public double Lat { get; set; }

        [JsonPropertyName("lng")]
        public double Lng { get; set; }
    }
}
