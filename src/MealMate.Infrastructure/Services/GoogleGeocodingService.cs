using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
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
                MessageCodes.Maps.ApiKeyMissing,
                "Google Maps server API key is not configured. Set GoogleMaps:ApiKey or GOOGLE_MAPS_SERVER_API_KEY.");
        }

        try
        {
            var url = $"{_options.GeocodeBaseUrl.TrimEnd('/')}{query}&key={Uri.EscapeDataString(apiKey)}";
            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google Geocoding HTTP {StatusCode}", (int)response.StatusCode);
                return Error.Validation(MessageCodes.Maps.GeocodeFailed, "Geocoding request failed.");
            }

            var payload = await response.Content.ReadFromJsonAsync<GoogleGeocodeResponse>(cancellationToken);
            if (payload is null || !string.Equals(payload.Status, "OK", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Google Geocoding status {Status}", payload?.Status);
                return Error.Validation(
                    MessageCodes.Maps.GeocodeUnresolved,
                    $"Could not resolve coordinates (Google status: {payload?.Status ?? "null"}).");
            }

            var results = payload.Results?
                .Where(r => !string.IsNullOrWhiteSpace(r.FormattedAddress) && r.Geometry?.Location is not null)
                .ToList();
            if (results is null || results.Count == 0)
            {
                return Error.Validation(
                    MessageCodes.Maps.GeocodeUnresolved,
                    "Could not resolve coordinates for the given address.");
            }

            // Prefer the richest Google result (street/route over bare plus-code pins),
            // then fill any missing fields from the other results.
            var ordered = results
                .OrderByDescending(ScoreResult)
                .ThenByDescending(r => r.AddressComponents?.Count ?? 0)
                .ToList();
            var primary = ordered[0];
            var location = primary.Geometry!.Location!;
            var parts = MergeComponents(ordered);
            var formatted = primary.FormattedAddress!.Trim();
            var block = parts.Block ?? ExtractKuwaitBlock(ordered.Select(r => r.FormattedAddress));
            var (street, avenue) = KuwaitAddressText.SplitStreetAndAvenue(parts.Street);
            // Mobile maps `building` → الجادة. Prefer extracted avenue over POI/premise names.
            var building = avenue ?? parts.Building;

            return new GoogleGeocodeResult(
                formatted,
                Convert.ToDecimal(location.Lat),
                Convert.ToDecimal(location.Lng),
                parts.Governorate,
                parts.Area,
                block,
                street,
                building);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Google Geocoding request error");
            return Error.Validation(MessageCodes.Maps.GeocodeFailed, "Geocoding request failed.");
        }
    }

    private static int ScoreResult(GoogleGeocodeApiResult result)
    {
        var components = result.AddressComponents;
        if (components is null || components.Count == 0) return 0;

        var score = 0;
        if (HasType(components, "route")) score += 6;
        if (HasType(components, "street_number")) score += 4;
        if (HasType(components, "premise") || HasType(components, "establishment")) score += 3;
        if (HasType(components, "sublocality_level_1") || HasType(components, "neighborhood")) score += 3;
        if (HasType(components, "sublocality_level_2")) score += 2;
        if (HasType(components, "locality")) score += 2;
        if (HasType(components, "administrative_area_level_1")) score += 1;
        if (HasType(components, "plus_code") && score < 4) score -= 3;
        return score;
    }

    private static bool HasType(IReadOnlyList<GoogleAddressComponent> components, string type)
        => components.Any(c =>
            c.Types is not null
            && c.Types.Any(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase)));

    private static (
        string? Governorate,
        string? Area,
        string? Block,
        string? Street,
        string? Building) MergeComponents(IReadOnlyList<GoogleGeocodeApiResult> orderedResults)
    {
        string? governorate = null, area = null, block = null, street = null, building = null;
        foreach (var result in orderedResults)
        {
            var parts = ParseComponents(result.AddressComponents);
            governorate ??= parts.Governorate;
            area ??= parts.Area;
            block ??= parts.Block;
            street ??= parts.Street;
            building ??= parts.Building;
            if (governorate is not null
                && area is not null
                && block is not null
                && street is not null
                && building is not null)
            {
                break;
            }
        }

        return (governorate, area, block, street, building);
    }

    private static string? ExtractKuwaitBlock(IEnumerable<string?> addresses)
    {
        foreach (var address in addresses)
        {
            if (string.IsNullOrWhiteSpace(address)) continue;
            var match = System.Text.RegularExpressions.Regex.Match(
                address,
                @"(?:قطعة|قطعه|block)\s*([0-9٠-٩]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (!match.Success) continue;
            return NormalizeEasternDigits(match.Groups[1].Value);
        }

        return null;
    }

    private static string NormalizeEasternDigits(string value)
    {
        var chars = value.Select(ch => ch switch
        {
            '٠' => '0',
            '١' => '1',
            '٢' => '2',
            '٣' => '3',
            '٤' => '4',
            '٥' => '5',
            '٦' => '6',
            '٧' => '7',
            '٨' => '8',
            '٩' => '9',
            _ => ch,
        }).ToArray();
        return new string(chars);
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
        var block = Get("sublocality_level_2");
        var neighborhood = Get("neighborhood");
        if (block is null
            && neighborhood is not null
            && !string.Equals(neighborhood, area, StringComparison.OrdinalIgnoreCase))
        {
            // In Kuwait, neighborhood text sometimes carries the block number.
            block = ExtractKuwaitBlock(new[] { neighborhood }) ?? neighborhood;
        }

        if (string.Equals(block, area, StringComparison.OrdinalIgnoreCase))
        {
            block = null;
        }

        var route = Get("route");
        var streetNumber = Get("street_number");
        var premise = Get("premise", "establishment");
        // Keep house/street_number out of the street field — Kuwait forms use street + avenue separately.
        // Avenue is split later via KuwaitAddressText (route often arrives as "شارع 2 جادة 2").
        var street = string.IsNullOrWhiteSpace(route) ? null : route.Trim();
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
