using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.AdminCoverage;

public sealed record GetAdminCoverageSnapshotQuery(
    string CountryCode,
    Guid? GovernorateId,
    Guid? RestaurantId,
    DateOnly ServiceDate,
    string? Search) : IQuery<CoverageSnapshotDto>;

public sealed class GetAdminCoverageSnapshotQueryHandler : IQueryHandler<GetAdminCoverageSnapshotQuery, CoverageSnapshotDto>
{
    private readonly IAppDbContext _db;
    private readonly AdminCoverageAccess _access;
    private readonly IDateTimeProvider _clock;

    public GetAdminCoverageSnapshotQueryHandler(IAppDbContext db, AdminCoverageAccess access, IDateTimeProvider clock)
        => (_db, _access, _clock) = (db, access, clock);

    public async Task<Result<CoverageSnapshotDto>> Handle(GetAdminCoverageSnapshotQuery request, CancellationToken cancellationToken)
    {
        if (request.ServiceDate == default)
            return Error.Validation("admin_coverage.service_date_required");

        var normalizedSearch = request.Search?.Trim() ?? string.Empty;
        var accessResult = await _access.ResolveAsync(request.CountryCode, cancellationToken);
        if (accessResult.IsFailure) return accessResult.Error;
        var access = accessResult.Value;
        var regions = access.Regions;
        var regionById = regions.ToDictionary(region => region.Id);
        var allRegionIds = regions.Select(region => region.Id).ToArray();

        var allLinks = allRegionIds.Length == 0
            ? []
            : await _db.RestaurantServiceRegions.AsNoTracking()
                .Where(link => allRegionIds.Contains(link.RegionId) && !link.IsDeleted)
                .ToListAsync(cancellationToken);
        var linkedRestaurantIds = allLinks.Select(link => link.RestaurantId).Distinct().ToArray();
        var profiles = await _db.RestaurantProfiles.AsNoTracking()
            .Where(profile => !profile.IsDeleted && !profile.IsArchived &&
                (profile.CountryId == access.Country.Id
                 || (profile.CountryId == null &&
                     ((profile.PrimaryRegionId != null && allRegionIds.Contains(profile.PrimaryRegionId.Value))
                      || linkedRestaurantIds.Contains(profile.Id)))))
            .ToListAsync(cancellationToken);

        var linksByRestaurant = allLinks.GroupBy(link => link.RestaurantId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var allowedProfiles = profiles.Where(profile =>
        {
            var restaurantLinks = linksByRestaurant.GetValueOrDefault(profile.Id) ?? [];
            var linkRegionIds = restaurantLinks.Select(link => link.RegionId).ToArray();
            return access.AllowsRestaurant(profile.Id, profile.PrimaryRegionId, linkRegionIds);
        }).ToDictionary(profile => profile.Id);

        if (request.RestaurantId is Guid requestedRestaurantId && !allowedProfiles.ContainsKey(requestedRestaurantId))
            return Error.Forbidden("admin_coverage.restaurant_not_allowed");

        var activeAccountUserIds = await _db.GetActiveAccountUserIdsAsync(
            allowedProfiles.Values.Select(profile => profile.UserId).Distinct().ToArray(), cancellationToken);

        var authorizedRegionIds = access.GetAuthorizedRegionIds().ToHashSet();
        foreach (var grant in access.Grants.Where(grant => grant.RestaurantId is not null && grant.RegionId is null && !grant.IsCountryWide))
        {
            if (grant.RestaurantId is Guid restaurantId && allowedProfiles.TryGetValue(restaurantId, out var profile))
            {
                AddRestaurantRegions(profile, linksByRestaurant.GetValueOrDefault(restaurantId) ?? [], regions, regionById, authorizedRegionIds);
            }
        }
        var scopedRegionIds = access.GetStructuralRegionIds(authorizedRegionIds).ToHashSet();

        if (request.GovernorateId is Guid governorateId)
        {
            var governorate = regions.FirstOrDefault(region => region.Id == governorateId && region.Type == RegionType.Governorate);
            if (governorate is null || !scopedRegionIds.Contains(governorateId))
                return Error.Forbidden("admin_coverage.governorate_not_allowed");
            authorizedRegionIds.RemoveWhere(regionId => !CoverageAccessContext.IsDescendantOrSelf(regionById.GetValueOrDefault(regionId), governorateId, regionById));
            scopedRegionIds = access.GetStructuralRegionIds(authorizedRegionIds).ToHashSet();
        }

        var candidateRestaurantIds = allowedProfiles.Keys.ToHashSet();
        if (request.RestaurantId is Guid restaurantFilter)
            candidateRestaurantIds.IntersectWith([restaurantFilter]);
        else if (request.GovernorateId is Guid selectedGovernorate)
            candidateRestaurantIds.RemoveWhere(restaurantId => !ServesRegion(
                linksByRestaurant.GetValueOrDefault(restaurantId) ?? [],
                allowedProfiles[restaurantId].PrimaryRegionId,
                selectedGovernorate,
                regionById));

        var searchRegionIds = string.IsNullOrEmpty(normalizedSearch)
            ? new HashSet<Guid>()
            : regions.Where(region => scopedRegionIds.Contains(region.Id) && Matches(normalizedSearch, region.NameAr, region.NameEn))
                .Select(region => region.Id).ToHashSet();
        var searchRestaurantIds = string.IsNullOrEmpty(normalizedSearch)
            ? new HashSet<Guid>()
            : candidateRestaurantIds.Where(restaurantId =>
            {
                var profile = allowedProfiles[restaurantId];
                return Matches(normalizedSearch, profile.TradeNameAr, profile.TradeNameEn, profile.LegalNameAr, profile.LegalNameEn)
                    || (linksByRestaurant.GetValueOrDefault(restaurantId) ?? []).Any(link => scopedRegionIds.Contains(link.RegionId) &&
                        regionById.TryGetValue(link.RegionId, out var region) && Matches(normalizedSearch, region.NameAr, region.NameEn));
            }).ToHashSet();

        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            if (searchRegionIds.Count == 0 && searchRestaurantIds.Count == 0)
            {
                scopedRegionIds.Clear();
                candidateRestaurantIds.Clear();
            }
            else
            {
                var regionMatchedRestaurants = candidateRestaurantIds.Where(restaurantId => ServesAnyRegion(
                    linksByRestaurant.GetValueOrDefault(restaurantId) ?? [],
                    allowedProfiles[restaurantId].PrimaryRegionId,
                    searchRegionIds,
                    regionById)).ToHashSet();
                candidateRestaurantIds.IntersectWith(searchRestaurantIds.Union(regionMatchedRestaurants));
                var servedRegionIds = new HashSet<Guid>();
                foreach (var restaurantId in candidateRestaurantIds)
                    AddRestaurantRegions(allowedProfiles[restaurantId], linksByRestaurant.GetValueOrDefault(restaurantId) ?? [], regions, regionById, servedRegionIds);
                scopedRegionIds.IntersectWith(searchRegionIds.Union(servedRegionIds));
                authorizedRegionIds.IntersectWith(scopedRegionIds);
            }
        }
        else if (request.RestaurantId is Guid selectedRestaurant)
        {
            var servedRegionIds = new HashSet<Guid>();
            AddRestaurantRegions(allowedProfiles[selectedRestaurant], linksByRestaurant.GetValueOrDefault(selectedRestaurant) ?? [], regions, regionById, servedRegionIds);
            scopedRegionIds.IntersectWith(servedRegionIds);
            authorizedRegionIds.IntersectWith(servedRegionIds);
        }

        var selectedProfiles = allowedProfiles.Values.Where(profile => candidateRestaurantIds.Contains(profile.Id)).ToArray();
        var selectedProfileIds = selectedProfiles.Select(profile => profile.Id).ToHashSet();
        var selectedRegions = regions.Where(region => scopedRegionIds.Contains(region.Id)).ToArray();
        var selectedLinks = allLinks.Where(link => selectedProfileIds.Contains(link.RestaurantId) && scopedRegionIds.Contains(link.RegionId)).ToArray();
        var restaurantDtos = selectedProfiles.Select(profile => new CoverageRestaurantDto(
            profile.Id,
            profile.TradeNameAr,
            profile.TradeNameEn,
            null,
            profile.IsOperationallyActive && !profile.IsArchived,
            activeAccountUserIds.Contains(profile.UserId),
            ValidPoint(profile.Latitude, profile.Longitude),
            null)).ToArray();
        var regionDtos = selectedRegions.Select(region => new CoverageRegionDto(
            region.Id,
            region.ParentRegionId,
            region.Type.ToString(),
            region.NameAr,
            region.NameEn,
            region.IsActive,
            ValidPoint(region.CentroidLatitude, region.CentroidLongitude),
            !authorizedRegionIds.Contains(region.Id))).ToArray();
        var linkDtos = selectedLinks.Select(link => new CoverageLinkDto(
            link.RestaurantId,
            link.RegionId,
            link.IsActive,
            link.DeliveryWindowStart.ToString("HH:mm"),
            link.DeliveryWindowEnd.ToString("HH:mm"))).ToArray();

        var resolved = CoverageRegionResolver.Resolve(
            selectedRegions.Select(region => new CoverageRegionNode(region.Id, region.ParentRegionId, region.Type, region.IsActive)),
            restaurantDtos.Select(restaurant => new CoverageRestaurantEligibility(restaurant.Id, restaurant.OperationallyActive, restaurant.AccountActive)),
            selectedLinks);
        var metricAreaIds = selectedRegions.Where(region => region.Type == RegionType.Area && authorizedRegionIds.Contains(region.Id))
            .Select(region => region.Id).ToHashSet();
        var metrics = CoverageRegionResolver.Metrics(resolved.Where(pair => metricAreaIds.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value));
        var filters = new CoverageFiltersDto(
            access.Country.Code,
            request.GovernorateId,
            request.RestaurantId,
            request.ServiceDate,
            normalizedSearch);

        return new CoverageSnapshotDto(_clock.UtcNow, filters, regionDtos, restaurantDtos, linkDtos,
            new CoverageMetricsDto(metrics.ActiveAreaCount, metrics.FullyCoveredAreaCount, metrics.PartiallyCoveredAreaCount,
                metrics.UncoveredAreaCount, metrics.SingleRestaurantAreaCount, metrics.FullyCoveredAreaPercent));
    }

    private static bool Matches(string search, params string?[] candidates)
        => candidates.Any(candidate => candidate?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);

    private static bool ServesRegion(
        IReadOnlyCollection<RestaurantServiceRegion> links,
        Guid? primaryRegionId,
        Guid regionId,
        IReadOnlyDictionary<Guid, Region> regionById)
        => (primaryRegionId is Guid primary &&
            (CoverageAccessContext.IsDescendantOrSelf(regionById.GetValueOrDefault(primary), regionId, regionById)
             || CoverageAccessContext.IsDescendantOrSelf(regionById.GetValueOrDefault(regionId), primary, regionById)))
           || links.Any(link =>
               CoverageAccessContext.IsDescendantOrSelf(regionById.GetValueOrDefault(link.RegionId), regionId, regionById)
               || CoverageAccessContext.IsDescendantOrSelf(regionById.GetValueOrDefault(regionId), link.RegionId, regionById));

    private static bool ServesAnyRegion(
        IReadOnlyCollection<RestaurantServiceRegion> links,
        Guid? primaryRegionId,
        IReadOnlySet<Guid> regionIds,
        IReadOnlyDictionary<Guid, Region> regionById)
        => regionIds.Any(regionId => ServesRegion(links, primaryRegionId, regionId, regionById));

    private static void AddRestaurantRegions(
        RestaurantProfile profile,
        IReadOnlyCollection<RestaurantServiceRegion> links,
        IReadOnlyCollection<Region> regions,
        IReadOnlyDictionary<Guid, Region> regionById,
        ISet<Guid> destination)
    {
        var roots = links.Select(link => link.RegionId).ToHashSet();
        if (profile.PrimaryRegionId is Guid primaryRegionId) roots.Add(primaryRegionId);
        foreach (var rootId in roots)
        {
            if (!regionById.TryGetValue(rootId, out var root)) continue;
            destination.Add(rootId);
            foreach (var region in regions)
                if (CoverageAccessContext.IsDescendantOrSelf(region, rootId, regionById)) destination.Add(region.Id);
            var current = root;
            var visited = new HashSet<Guid>();
            while (current.ParentRegionId is Guid parentId && visited.Add(current.Id) && regionById.TryGetValue(parentId, out var parent))
            {
                destination.Add(parent.Id);
                current = parent;
            }
        }
    }

    private static MapPointDto? ValidPoint(decimal? latitude, decimal? longitude)
    {
        if (latitude is not decimal lat || longitude is not decimal lng
            || lat < -90m || lat > 90m || lng < -180m || lng > 180m)
            return null;
        return new MapPointDto(lat, lng);
    }
}

