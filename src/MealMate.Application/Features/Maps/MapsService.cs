using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.Maps;

public sealed class MapsService : IMapsService
{
    private readonly IGoogleGeocodingService _geocoding;
    private readonly IAppDbContext _db;

    public MapsService(IGoogleGeocodingService geocoding, IAppDbContext db)
    {
        _geocoding = geocoding;
        _db = db;
    }

    public async Task<Result<GoogleGeocodeResult>> GeocodeAddressAsync(
        string? address,
        string? countryCode,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return Error.Validation(MessageCodes.Validation.RequiredField, "Address is required.");
        }

        var result = await _geocoding.GeocodeAddressAsync(
            address,
            string.IsNullOrWhiteSpace(countryCode) ? "KW" : countryCode,
            ct);

        if (result.IsFailure) return result;

        var enriched = await MapPlaceRegionSuggestion.EnrichAsync(_db, result.Value, ct);
        return Result.Success(enriched);
    }

    public async Task<Result<GoogleGeocodeResult>> ReverseGeocodeAsync(
        decimal latitude,
        decimal longitude,
        CancellationToken ct = default)
    {
        var result = await _geocoding.ReverseGeocodeAsync(latitude, longitude, ct);
        if (result.IsFailure) return result;

        var enriched = await MapPlaceRegionSuggestion.EnrichAsync(_db, result.Value, ct);
        return Result.Success(enriched);
    }
}
