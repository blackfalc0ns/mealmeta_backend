namespace MealMate.Application.Features.VoiceCalls.Services;

/// <summary>
/// Pure policy math for delivery-contact rules. Boundary behavior is fixed here:
/// the radius limit is inclusive, invalid coordinates are rejected by range checks,
/// and (0,0) is a geographically valid point — never a blanket rejection reason.
/// </summary>
public static class VoiceCallPolicyMath
{
    private const double EarthRadiusMeters = 6_371_000d;

    public static bool IsWithinContactRadius(double distanceMeters, double maxMeters)
    {
        if (!IsFiniteNonNegative(distanceMeters) || !IsFiniteNonNegative(maxMeters))
        {
            return false;
        }

        return distanceMeters <= maxMeters;
    }

    /// <summary>
    /// Haversine distance in meters. Returns null when any coordinate is missing,
    /// non-finite, or outside its valid range — callers translate null into an
    /// explicit deny reason instead of silently treating it as zero.
    /// </summary>
    public static double? HaversineMeters(
        double fromLatitudeDegrees,
        double fromLongitudeDegrees,
        double toLatitudeDegrees,
        double toLongitudeDegrees)
    {
        if (!IsValidLatitude(fromLatitudeDegrees) || !IsValidLatitude(toLatitudeDegrees) ||
            !IsValidLongitude(fromLongitudeDegrees) || !IsValidLongitude(toLongitudeDegrees))
        {
            return null;
        }

        var fromLat = DegreesToRadians(fromLatitudeDegrees);
        var toLat = DegreesToRadians(toLatitudeDegrees);
        var deltaLat = DegreesToRadians(toLatitudeDegrees - fromLatitudeDegrees);
        var deltaLon = DegreesToRadians(toLongitudeDegrees - fromLongitudeDegrees);

        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2)
                + Math.Cos(fromLat) * Math.Cos(toLat) * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        var distance = EarthRadiusMeters * c;
        return double.IsFinite(distance) && distance >= 0 ? distance : null;
    }

    public static bool IsValidLatitude(double value) => IsFinite(value) && value is >= -90d and <= 90d;

    public static bool IsValidLongitude(double value) => IsFinite(value) && value is >= -180d and <= 180d;

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool IsFiniteNonNegative(double value) => IsFinite(value) && value >= 0;

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}
