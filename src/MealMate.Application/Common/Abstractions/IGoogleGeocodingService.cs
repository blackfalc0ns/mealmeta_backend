using MealMate.Application.Common.Models;

namespace MealMate.Application.Common.Abstractions;

public sealed record GoogleGeocodeResult(
    string FormattedAddress,
    decimal Latitude,
    decimal Longitude,
    string? Governorate = null,
    string? Area = null,
    string? Block = null,
    string? Street = null,
    string? Building = null,
    Guid? SuggestedRegionId = null,
    string? SuggestedRegionNameAr = null,
    string? SuggestedRegionNameEn = null,
    Guid? SuggestedGovernorateId = null,
    string? SuggestedGovernorateNameAr = null,
    string? SuggestedGovernorateNameEn = null,
    Guid? SuggestedBlockId = null,
    string? SuggestedBlockNameAr = null,
    string? SuggestedBlockNameEn = null);

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
