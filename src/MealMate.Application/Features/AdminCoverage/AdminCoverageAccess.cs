using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.Catalog;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.AdminCoverage;

public sealed record CoverageAccessContext(
    Guid TenantId,
    Country Country,
    IReadOnlyList<Region> Regions,
    IReadOnlyList<CoverageScopeGrant> Grants,
    bool IsSuperAdmin)
{
    public IReadOnlySet<Guid> GetAuthorizedRegionIds()
    {
        if (IsSuperAdmin || Grants.Any(grant => grant.IsCountryWide))
            return Regions.Select(region => region.Id).ToHashSet();

        var byId = Regions.ToDictionary(region => region.Id);
        var result = new HashSet<Guid>();
        foreach (var grant in Grants)
        {
            if (grant.RegionId is not Guid rootId || !byId.TryGetValue(rootId, out var root)) continue;
            foreach (var region in Regions)
            {
                if (IsDescendantOrSelf(region, rootId, byId)) result.Add(region.Id);
            }
        }
        return result;
    }

    public IReadOnlySet<Guid> GetStructuralRegionIds(IEnumerable<Guid> authorizedRegionIds)
    {
        var byId = Regions.ToDictionary(region => region.Id);
        var result = authorizedRegionIds.ToHashSet();
        foreach (var id in result.ToArray())
            if (byId.TryGetValue(id, out var region)) AddAncestors(region, byId, result);
        return result;
    }

    public bool AllowsRestaurant(Guid restaurantId, Guid? primaryRegionId, IReadOnlyCollection<Guid> serviceRegionIds)
    {
        if (IsSuperAdmin) return true;
        var byId = Regions.ToDictionary(region => region.Id);
        foreach (var grant in Grants)
        {
            if (grant.RestaurantId is Guid scopedRestaurant && scopedRestaurant != restaurantId) continue;
            if (grant.RegionId is not Guid scopeRegionId)
            {
                if (grant.IsCountryWide || grant.RestaurantId == restaurantId) return true;
                continue;
            }

            if (!byId.ContainsKey(scopeRegionId)) continue;
            var locationMatches = primaryRegionId is Guid primary &&
                (IsDescendantOrSelf(byId.GetValueOrDefault(primary), scopeRegionId, byId)
                 || IsDescendantOrSelf(byId.GetValueOrDefault(scopeRegionId), primary, byId));
            var serviceMatches = serviceRegionIds.Any(serviceRegionId =>
                IsDescendantOrSelf(byId.GetValueOrDefault(serviceRegionId), scopeRegionId, byId)
                || IsDescendantOrSelf(byId.GetValueOrDefault(scopeRegionId), serviceRegionId, byId));
            if (locationMatches || serviceMatches) return true;
        }
        return false;
    }

    public static bool IsDescendantOrSelf(Region? region, Guid ancestorId, IReadOnlyDictionary<Guid, Region> byId)
    {
        var current = region;
        var visited = new HashSet<Guid>();
        while (current is not null && visited.Add(current.Id))
        {
            if (current.Id == ancestorId) return true;
            if (current.ParentRegionId is not Guid parentId || !byId.TryGetValue(parentId, out current)) return false;
        }
        return false;
    }

    private static void AddAncestors(Region region, IReadOnlyDictionary<Guid, Region> byId, ISet<Guid> destination)
    {
        var current = region;
        var visited = new HashSet<Guid>();
        while (current.ParentRegionId is Guid parentId && visited.Add(current.Id) && byId.TryGetValue(parentId, out var parent))
        {
            destination.Add(parent.Id);
            current = parent;
        }
    }
}

public sealed class AdminCoverageAccess
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly KuwaitTenantResolver _tenantResolver;

    public AdminCoverageAccess(IAppDbContext db, ICurrentUser currentUser, KuwaitTenantResolver tenantResolver)
        => (_db, _currentUser, _tenantResolver) = (db, currentUser, tenantResolver);

    public async Task<Result<CoverageAccessContext>> ResolveAsync(
        string countryCode,
        CancellationToken cancellationToken,
        string requiredPermission = PermissionCodes.RestaurantRegView)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Error.Unauthorized("admin_coverage.authentication_required");
        if (_currentUser.UserType != UserType.Admin || !_currentUser.Roles.Any(AppRoles.IsAdminRole))
            return Error.Forbidden("admin_coverage.admin_role_required");
        var isSuperAdmin = _currentUser.IsInRole(AppRoles.SuperAdmin);
        if (!isSuperAdmin && !_currentUser.HasPermission(requiredPermission))
            return Error.Forbidden("admin_coverage.permission_required");

        var normalizedCountryCode = countryCode.Trim().ToUpperInvariant();
        if (normalizedCountryCode != "KW")
            return Error.Forbidden("admin_coverage.country_not_allowed");

        var tenantResult = await _tenantResolver.ResolveAsync(isSuperAdmin, cancellationToken);
        if (tenantResult.IsFailure) return tenantResult.Error;
        var tenant = tenantResult.Value;
        var country = await _db.Countries.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == tenant.CountryId && item.Code == normalizedCountryCode && item.IsActive && !item.IsDeleted, cancellationToken);
        if (country is null) return Error.Forbidden("admin_coverage.country_not_allowed");

        var regions = await _db.Regions.AsNoTracking()
            .Where(region => region.CountryId == country.Id && !region.IsDeleted)
            .ToListAsync(cancellationToken);
        if (isSuperAdmin)
            return new CoverageAccessContext(tenant.Id, country, regions, [], true);

        var scopeIds = _currentUser.AccessScopeIds.Distinct().ToArray();
        if (scopeIds.Length == 0) return Error.Forbidden("admin_coverage.scope_required");
        var scopes = await _db.AccessScopes.AsNoTracking()
            .Where(scope => scopeIds.Contains(scope.Id) && scope.IsActive && !scope.IsDeleted)
            .ToListAsync(cancellationToken);
        var regionById = regions.ToDictionary(region => region.Id);
        var grants = scopes
            .Select(scope => CoverageScopeGrant.TryCreate(scope, tenant.Id, country.Id))
            .Where(grant => grant is not null)
            .Select(grant => grant!)
            .Where(grant => grant.RegionId is null || IsActiveRegionHierarchy(grant.RegionId.Value, regionById))
            .ToArray();
        if (grants.Length == 0) return Error.Forbidden("admin_coverage.scope_required");

        return new CoverageAccessContext(tenant.Id, country, regions, grants, false);
    }

    private static bool IsActiveRegionHierarchy(Guid regionId, IReadOnlyDictionary<Guid, Region> byId)
    {
        var currentId = regionId;
        var visited = new HashSet<Guid>();
        while (visited.Add(currentId) && byId.TryGetValue(currentId, out var current))
        {
            if (!current.IsActive) return false;
            if (current.ParentRegionId is not Guid parentId) return true;
            currentId = parentId;
        }
        return false;
    }
}
