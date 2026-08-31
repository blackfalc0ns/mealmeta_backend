using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Maps;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/maps")]
public sealed class MapsController : ApiControllerBase
{
    private readonly IGoogleGeocodingService _geocoding;
    private readonly IAppDbContext _db;

    public MapsController(IGoogleGeocodingService geocoding, IAppDbContext db)
    {
        _geocoding = geocoding;
        _db = db;
    }

    /// <summary>
    /// Resolve latitude/longitude and address components using the server Google key.
    /// Anonymous so restaurant/customer onboarding can resolve location before login.
    /// </summary>
    [HttpPost("geocode")]
    [AllowAnonymous]
    public async Task<IActionResult> Geocode(
        [FromBody] GeocodeAddressRequest body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Address))
        {
            return HandleResult(Result.Failure<GoogleGeocodeResult>(
                Error.Validation(MessageCodes.Validation.RequiredField, "Address is required.")));
        }

        var result = await _geocoding.GeocodeAddressAsync(
            body.Address,
            string.IsNullOrWhiteSpace(body.CountryCode) ? "KW" : body.CountryCode,
            cancellationToken);
        return await HandleEnrichedAsync(result, cancellationToken);
    }

    /// <summary>
    /// Resolve a formatted address and components from coordinates using the server Google key.
    /// </summary>
    [HttpPost("reverse-geocode")]
    [AllowAnonymous]
    public async Task<IActionResult> ReverseGeocode(
        [FromBody] ReverseGeocodeRequest body,
        CancellationToken cancellationToken)
    {
        var result = await _geocoding.ReverseGeocodeAsync(body.Latitude, body.Longitude, cancellationToken);
        return await HandleEnrichedAsync(result, cancellationToken);
    }

    private async Task<IActionResult> HandleEnrichedAsync(
        Result<GoogleGeocodeResult> result,
        CancellationToken cancellationToken)
    {
        if (result.IsFailure)
        {
            return HandleResult(result);
        }

        var enriched = await MapPlaceRegionSuggestion.EnrichAsync(_db, result.Value, cancellationToken);
        return HandleResult(Result.Success(enriched));
    }
}

public sealed record GeocodeAddressRequest(string Address, string? CountryCode = "KW");

public sealed record ReverseGeocodeRequest(decimal Latitude, decimal Longitude);
