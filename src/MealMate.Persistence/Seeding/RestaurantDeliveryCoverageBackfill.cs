using MealMate.Application.Common.Geography;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Persistence.Seeding;

/// <summary>
/// Applies catalog Area centroids and normalizes existing restaurant coverage to Area IDs
/// so customer map pins can match delivery service regions.
/// </summary>
public static class RestaurantDeliveryCoverageBackfill
{
    public const int NearbyAreaLimit = 12;

    public static async Task SeedAsync(AppDbContext db, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        var areas = await db.Regions
            .Where(x => x.IsActive && x.Type == RegionType.Area)
            .ToListAsync(cancellationToken);
        if (areas.Count == 0)
        {
            logger?.LogWarning("Restaurant coverage backfill skipped: no active Area rows.");
            return;
        }

        var areaById = areas.ToDictionary(x => x.Id);
        var blocks = await db.Regions
            .AsNoTracking()
            .Where(x => x.Type == RegionType.Block)
            .Select(x => new { x.Id, x.ParentRegionId })
            .ToListAsync(cancellationToken);
        var blockParent = blocks
            .Where(x => x.ParentRegionId is not null)
            .ToDictionary(x => x.Id, x => x.ParentRegionId!.Value);
        var areasByGovernorate = areas
            .Where(x => x.ParentRegionId is not null)
            .GroupBy(x => x.ParentRegionId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

        var restaurants = await db.RestaurantProfiles
            .Where(x => !x.IsArchived)
            .ToListAsync(cancellationToken);
        var coverage = await db.RestaurantServiceRegions.ToListAsync(cancellationToken);
        var coverageByRestaurant = coverage
            .GroupBy(x => x.RestaurantId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var added = 0;
        var reactivated = 0;
        var coordsUpdated = 0;

        foreach (var restaurant in restaurants)
        {
            coverageByRestaurant.TryGetValue(restaurant.Id, out var rows);
            rows ??= [];

            var targetAreaIds = new HashSet<Guid>();
            foreach (var row in rows)
            {
                if (areaById.ContainsKey(row.RegionId))
                {
                    targetAreaIds.Add(row.RegionId);
                    continue;
                }

                if (blockParent.TryGetValue(row.RegionId, out var parentAreaId) && areaById.ContainsKey(parentAreaId))
                {
                    targetAreaIds.Add(parentAreaId);
                    continue;
                }

                if (areasByGovernorate.TryGetValue(row.RegionId, out var childAreas))
                {
                    foreach (var id in childAreas)
                        targetAreaIds.Add(id);
                }
            }

            EnsureRestaurantCoordinates(restaurant, areaById, targetAreaIds, ref coordsUpdated);

            if (targetAreaIds.Count == 0)
            {
                foreach (var nearby in NearbyAreas(restaurant, areas).Take(NearbyAreaLimit))
                    targetAreaIds.Add(nearby.Id);
            }

            if (restaurant.PrimaryRegionId is null || !areaById.ContainsKey(restaurant.PrimaryRegionId.Value))
            {
                var primary = NearbyAreas(restaurant, areas).FirstOrDefault()
                    ?? areas.FirstOrDefault(x => x.Code == "KW-SL")
                    ?? areas[0];
                restaurant.PrimaryRegionId = primary.Id;
            }

            foreach (var areaId in targetAreaIds)
            {
                var existing = rows.FirstOrDefault(x => x.RegionId == areaId);
                if (existing is null)
                {
                    db.RestaurantServiceRegions.Add(new RestaurantServiceRegion
                    {
                        RestaurantId = restaurant.Id,
                        RegionId = areaId,
                        IsActive = true,
                        DeliveryWindowStart = new TimeOnly(8, 0),
                        DeliveryWindowEnd = new TimeOnly(16, 0),
                        PreparationLeadTimeMinutes = 90,
                    });
                    added++;
                    continue;
                }

                if (!existing.IsActive)
                {
                    existing.IsActive = true;
                    reactivated++;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger?.LogInformation(
            "Restaurant coverage backfill finished. Restaurants={RestaurantCount}, areasAdded={Added}, reactivated={Reactivated}, coordsUpdated={Coords}.",
            restaurants.Count, added, reactivated, coordsUpdated);
    }

    private static void EnsureRestaurantCoordinates(
        RestaurantProfile restaurant,
        IReadOnlyDictionary<Guid, Region> areaById,
        IReadOnlySet<Guid> targetAreaIds,
        ref int coordsUpdated)
    {
        if (restaurant.Latitude is not null && restaurant.Longitude is not null)
            return;

        Region? source = null;
        if (restaurant.PrimaryRegionId is Guid primaryId)
            areaById.TryGetValue(primaryId, out source);
        if (source is null)
        {
            foreach (var id in targetAreaIds)
            {
                if (areaById.TryGetValue(id, out source))
                    break;
            }
        }

        if (source?.CentroidLatitude is null || source.CentroidLongitude is null)
            return;

        restaurant.Latitude = source.CentroidLatitude;
        restaurant.Longitude = source.CentroidLongitude;
        coordsUpdated++;
    }

    private static IEnumerable<Region> NearbyAreas(RestaurantProfile restaurant, IReadOnlyList<Region> areas)
    {
        if (restaurant.Latitude is not decimal lat || restaurant.Longitude is not decimal lng)
        {
            return areas
                .Where(x => x.CentroidLatitude is not null && x.CentroidLongitude is not null)
                .OrderBy(x => x.Code == "KW-SL" ? 0 : x.Code == "KW-HW" ? 1 : 2);
        }

        return areas
            .Where(x => x.CentroidLatitude is not null && x.CentroidLongitude is not null)
            .Select(x => new
            {
                Area = x,
                DistanceKm = KuwaitGeoBounds.HaversineKm(lat, lng, x.CentroidLatitude!.Value, x.CentroidLongitude!.Value),
            })
            .Where(x => x.DistanceKm <= KuwaitGeoBounds.MaxSuggestionDistanceKm)
            .OrderBy(x => x.DistanceKm)
            .Select(x => x.Area);
    }
}
