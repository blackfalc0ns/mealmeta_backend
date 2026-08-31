using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/maps")]
public sealed class MapsController : ApiControllerBase
{
    private readonly IGoogleGeocodingService _geocoding;

    public MapsController(IGoogleGeocodingService geocoding)
    {
        _geocoding = geocoding;
    }

    /// <summary>
    /// Resolve latitude/longitude from a required address using the server Google key.
    /// Anonymous so restaurant onboarding can resolve HQ before OTP/login.
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

        return HandleResult(await _geocoding.GeocodeAddressAsync(
            body.Address,
            string.IsNullOrWhiteSpace(body.CountryCode) ? "KW" : body.CountryCode,
            cancellationToken));
    }

    /// <summary>
    /// Resolve a formatted address from coordinates using the server Google key.
    /// Anonymous so map pin / live GPS reverse-geocode works during onboarding.
    /// </summary>
    [HttpPost("reverse-geocode")]
    [AllowAnonymous]
    public async Task<IActionResult> ReverseGeocode(
        [FromBody] ReverseGeocodeRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await _geocoding.ReverseGeocodeAsync(body.Latitude, body.Longitude, cancellationToken));
}

public sealed record GeocodeAddressRequest(string Address, string? CountryCode = "KW");

public sealed record ReverseGeocodeRequest(decimal Latitude, decimal Longitude);
