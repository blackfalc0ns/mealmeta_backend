using MealMate.Api.Common;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Maps;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/maps")]
public sealed class MapsController : ApiControllerBase
{
    private readonly IMapsService _mapsService;

    public MapsController(IMapsService mapsService)
    {
        _mapsService = mapsService;
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
        => HandleResult(await _mapsService.GeocodeAddressAsync(body.Address, body.CountryCode, cancellationToken));

    /// <summary>
    /// Resolve a formatted address and components from coordinates using the server Google key.
    /// </summary>
    [HttpPost("reverse-geocode")]
    [AllowAnonymous]
    public async Task<IActionResult> ReverseGeocode(
        [FromBody] ReverseGeocodeRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await _mapsService.ReverseGeocodeAsync(body.Latitude, body.Longitude, cancellationToken));
}
