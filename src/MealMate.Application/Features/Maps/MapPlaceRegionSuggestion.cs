using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Maps;

public static class MapPlaceRegionSuggestion
{
    public static async Task<GoogleGeocodeResult> EnrichAsync(
        IAppDbContext db,
        GoogleGeocodeResult place,
        CancellationToken cancellationToken = default)
    {
        var areas = await db.Regions
            .AsNoTracking()
            .Where(r => r.IsActive
                        && r.Type == RegionType.Area
                        && r.CentroidLatitude != null
                        && r.CentroidLongitude != null)
            .Select(r => new
            {
                r.Id,
                r.NameAr,
                r.NameEn,
                Latitude = r.CentroidLatitude!.Value,
                Longitude = r.CentroidLongitude!.Value,
            })
            .ToListAsync(cancellationToken);

        if (areas.Count == 0)
        {
            return place;
        }

        var areaHint = place.Area?.Trim();
        if (!string.IsNullOrWhiteSpace(areaHint))
        {
            var byName = areas.FirstOrDefault(a =>
                NamesMatch(a.NameAr, areaHint) || NamesMatch(a.NameEn, areaHint));
            if (byName is not null)
            {
                return place with
                {
                    SuggestedRegionId = byName.Id,
                    SuggestedRegionNameAr = byName.NameAr,
                    SuggestedRegionNameEn = byName.NameEn,
                };
            }
        }

        var nearest = areas
            .Select(a => new
            {
                Area = a,
                DistanceKm = KuwaitGeoBounds.HaversineKm(
                    place.Latitude, place.Longitude, a.Latitude, a.Longitude),
            })
            .OrderBy(x => x.DistanceKm)
            .First();

        if (nearest.DistanceKm > KuwaitGeoBounds.MaxSuggestionDistanceKm)
        {
            return place;
        }

        return place with
        {
            SuggestedRegionId = nearest.Area.Id,
            SuggestedRegionNameAr = nearest.Area.NameAr,
            SuggestedRegionNameEn = nearest.Area.NameEn,
        };
    }

    private static bool NamesMatch(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left)) return false;
        var a = Normalize(left);
        var b = Normalize(right);
        return a == b || a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal);
    }

    private static string Normalize(string value)
        => value.Trim()
            .Replace("أ", "ا", StringComparison.Ordinal)
            .Replace("إ", "ا", StringComparison.Ordinal)
            .Replace("آ", "ا", StringComparison.Ordinal)
            .Replace("ة", "ه", StringComparison.Ordinal)
            .Replace("ى", "ي", StringComparison.Ordinal);
}
