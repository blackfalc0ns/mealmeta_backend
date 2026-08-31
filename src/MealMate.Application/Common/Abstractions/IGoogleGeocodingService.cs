using MealMate.Application.Common.Models;

namespace MealMate.Application.Common.Abstractions;

public sealed record GoogleGeocodeResult(
    string FormattedAddress,
    decimal Latitude,
    decimal Longitude);

public interface IGoogleGeocodingService
{
    Task<Result<GoogleGeocodeResult>> GeocodeAddressAsync(
        string address,
        string countryCode = "KW",
        CancellationToken cancellationToken = default);

    Task<Result<GoogleGeocodeResult>> ReverseGeocodeAsync(
        decimal latitude,
        decimal longitude,
        CancellationToken cancellationToken = default);
}
