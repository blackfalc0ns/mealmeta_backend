using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.RestaurantOperations;

namespace MealMate.Application.Features.AdminCoverage;

public sealed record CoverageRestaurantEligibility(Guid RestaurantId, bool OperationallyActive, bool AccountActive);

public sealed record CoverageAreaResult(IReadOnlyList<Guid> FullRestaurantIds, IReadOnlyList<Guid> PartialRestaurantIds);

public sealed record CoverageRegionNode(Guid Id, Guid? ParentId, RegionType Type, bool IsActive);

public sealed record CoverageMetrics(
    int ActiveAreaCount,
    int FullyCoveredAreaCount,
    int PartiallyCoveredAreaCount,
    int UncoveredAreaCount,
    int SingleRestaurantAreaCount,
    decimal? FullyCoveredAreaPercent);

public static class CoverageRegionResolver
{
    public static IReadOnlyDictionary<Guid, CoverageAreaResult> Resolve(
        IEnumerable<Region> regions,
        IEnumerable<CoverageRestaurantEligibility> restaurants,
        IEnumerable<RestaurantServiceRegion> links)
    {
        return Resolve(regions.Select(region => new CoverageRegionNode(region.Id, region.ParentRegionId, region.Type, region.IsActive)), restaurants, links);
    }

    public static IReadOnlyDictionary<Guid, CoverageAreaResult> Resolve(
        IEnumerable<CoverageRegionNode> regions,
        IEnumerable<CoverageRestaurantEligibility> restaurants,
        IEnumerable<RestaurantServiceRegion> links)
    {
        var regionList = regions.ToArray();
        var byId = regionList.ToDictionary(region => region.Id);
        var eligible = restaurants.Where(restaurant => restaurant.OperationallyActive && restaurant.AccountActive)
            .Select(restaurant => restaurant.RestaurantId).ToHashSet();
        var areas = regionList.Where(region => region.Type == RegionType.Area && IsActiveHierarchy(region, byId)).ToArray();
        var full = areas.ToDictionary(area => area.Id, _ => new HashSet<Guid>());
        var partial = areas.ToDictionary(area => area.Id, _ => new HashSet<Guid>());

        foreach (var link in links)
        {
            if (!link.IsActive || !eligible.Contains(link.RestaurantId) || !byId.TryGetValue(link.RegionId, out var linkedRegion) || !IsActiveHierarchy(linkedRegion, byId))
            {
                continue;
            }

            foreach (var area in areas)
            {
                if (linkedRegion.Id == area.Id || linkedRegion.Id == area.ParentId)
                {
                    full[area.Id].Add(link.RestaurantId);
                }
                else if (linkedRegion.Type == RegionType.Block && linkedRegion.ParentId == area.Id)
                {
                    partial[area.Id].Add(link.RestaurantId);
                }
            }
        }

        return areas.ToDictionary(
            area => area.Id,
            area => new CoverageAreaResult(
                full[area.Id].Order().ToArray(),
                partial[area.Id].Except(full[area.Id]).Order().ToArray()));
    }

    public static CoverageMetrics Metrics(IReadOnlyDictionary<Guid, CoverageAreaResult> resolved)
    {
        var full = resolved.Values.Count(area => area.FullRestaurantIds.Count > 0);
        var partial = resolved.Values.Count(area => area.FullRestaurantIds.Count == 0 && area.PartialRestaurantIds.Count > 0);
        var uncovered = resolved.Count - full - partial;
        var singleRestaurant = resolved.Values.Count(area => area.FullRestaurantIds.Count == 1);
        return new CoverageMetrics(
            resolved.Count,
            full,
            partial,
            uncovered,
            singleRestaurant,
            resolved.Count == 0 ? null : (decimal)full * 100m / resolved.Count);
    }

    private static bool IsActiveHierarchy(CoverageRegionNode region, IReadOnlyDictionary<Guid, CoverageRegionNode> byId)
    {
        var visited = new HashSet<Guid>();
        CoverageRegionNode? current = region;
        while (current is not null)
        {
            if (!current.IsActive || !visited.Add(current.Id)) return false;
            if (current.ParentId is not Guid parentId) return true;
            if (!byId.TryGetValue(parentId, out var parent)) return false;
            current = parent;
        }
        return false;
    }
}
