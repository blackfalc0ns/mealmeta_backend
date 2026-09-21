namespace MealMate.Infrastructure.Services;

public sealed class GoogleMapsOptions
{
    public const string SectionName = "GoogleMaps";

    /// <summary>
    /// Server-restricted Google API key for Geocoding (IP restriction recommended).
    /// Do not use the browser Maps JavaScript key here.
    /// </summary>
    public string? ApiKey { get; set; }

    public string GeocodeBaseUrl { get; set; } = "https://maps.googleapis.com/maps/api/geocode/json";
    public string DirectionsBaseUrl { get; set; } = "https://maps.googleapis.com/maps/api/directions/json";
    public string DistanceMatrixBaseUrl { get; set; } = "https://maps.googleapis.com/maps/api/distancematrix/json";
}
