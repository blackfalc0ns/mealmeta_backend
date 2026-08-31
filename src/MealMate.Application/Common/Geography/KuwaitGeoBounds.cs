namespace MealMate.Application.Common.Geography;

/// <summary>Loose Kuwait bounding box used to reject clearly invalid map pins.</summary>
public static class KuwaitGeoBounds
{
    public const decimal MinLatitude = 28.5m;
    public const decimal MaxLatitude = 30.2m;
    public const decimal MinLongitude = 46.5m;
    public const decimal MaxLongitude = 48.5m;

    /// <summary>Maximum distance (km) when suggesting a catalog Area from a map pin.</summary>
    public const double MaxSuggestionDistanceKm = 20d;

    public static bool IsWithin(decimal? latitude, decimal? longitude)
    {
        if (latitude is null || longitude is null) return false;
        return latitude is >= MinLatitude and <= MaxLatitude
               && longitude is >= MinLongitude and <= MaxLongitude;
    }

    public static double HaversineKm(decimal lat1, decimal lng1, decimal lat2, decimal lng2)
    {
        const double r = 6371d;
        static double ToRad(decimal deg) => (double)deg * Math.PI / 180d;
        var dLat = ToRad(lat2 - lat1);
        var dLng = ToRad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
                  * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
