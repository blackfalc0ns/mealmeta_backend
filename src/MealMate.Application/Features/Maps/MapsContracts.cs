namespace MealMate.Application.Features.Maps;

public sealed record GeocodeAddressRequest(string Address, string? CountryCode = "KW");

public sealed record ReverseGeocodeRequest(decimal Latitude, decimal Longitude);
