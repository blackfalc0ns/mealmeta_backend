using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.Maps;

public interface IMapsService
{
    Task<Result<GoogleGeocodeResult>> GeocodeAddressAsync(
        string? address,
        string? countryCode,
        CancellationToken ct = default);

    Task<Result<GoogleGeocodeResult>> ReverseGeocodeAsync(
        decimal latitude,
        decimal longitude,
        CancellationToken ct = default);
}
