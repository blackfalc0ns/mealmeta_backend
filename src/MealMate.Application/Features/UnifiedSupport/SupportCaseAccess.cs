using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.UnifiedSupport;

internal sealed class SupportCaseAccess(IAppDbContext db, ICurrentUser currentUser)
{
    public Guid? ActorId => currentUser.UserId;

    public bool IsSuperAdmin => currentUser.IsInRole(AppRoles.SuperAdmin);
    public bool IsSupportAdmin => currentUser.UserType == UserType.Admin && currentUser.HasPermission(PermissionCodes.SupportView);

    public bool CanReply => IsSuperAdmin || (IsSupportAdmin && currentUser.HasPermission(PermissionCodes.SupportReply));
    public bool CanManage => IsSuperAdmin || (IsSupportAdmin && currentUser.HasPermission(PermissionCodes.SupportManage));
    public bool CanWriteInternal => IsSuperAdmin || (IsSupportAdmin && currentUser.HasPermission(PermissionCodes.SupportInternal));

    public async Task<bool> HasActiveDriverIdentityAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserType != UserType.Driver || ActorId is not { } actorId)
            return false;
        if (!await db.DriverProfiles.AsNoTracking().AnyAsync(x => x.UserId == actorId, ct)) return false;
        return (await db.GetActiveAccountUserIdsAsync([actorId], ct)).Contains(actorId);
    }

    public async Task<bool> HasPersistedPermissionAsync(string permissionCode, CancellationToken ct)
    {
        if (ActorId is not { } actorId) return false;
        var candidate = (await db.GetActiveSupportOperatorCandidatesAsync(ct)).SingleOrDefault(x => x.UserId == actorId);
        return candidate is not null && (candidate.Roles.Contains(AppRoles.SuperAdmin)
            || candidate.UserType == UserType.Admin && candidate.PermissionCodes.Contains(permissionCode));
    }

    public async Task<bool> IsPersistedPlatformOperatorForFirstResponseAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || ActorId is not { } actorId) return false;
        var candidate = (await db.GetActiveSupportOperatorCandidatesAsync(ct))
            .SingleOrDefault(x => x.UserId == actorId);
        if (candidate is null || candidate.UserType != UserType.Admin) return false;
        return candidate.Roles.Contains(AppRoles.SuperAdmin)
            || candidate.PermissionCodes.Contains(PermissionCodes.SupportView)
                && (candidate.PermissionCodes.Contains(PermissionCodes.SupportReply)
                    || candidate.PermissionCodes.Contains(PermissionCodes.SupportManage));
    }

    public async Task<IReadOnlyList<SupportOperatorCandidate>> GetEligibleOperatorsAsync(
        SupportCase supportCase, bool requireManage, CancellationToken ct)
    {
        var candidates = await db.GetActiveSupportOperatorCandidatesAsync(ct);
        var eligible = new List<SupportOperatorCandidate>();
        foreach (var candidate in candidates)
        {
            var superAdmin = candidate.Roles.Contains(AppRoles.SuperAdmin);
            if (!superAdmin && (candidate.UserType != UserType.Admin
                || !candidate.PermissionCodes.Contains(PermissionCodes.SupportView)
                || !(requireManage
                    ? candidate.PermissionCodes.Contains(PermissionCodes.SupportManage)
                    : candidate.PermissionCodes.Contains(PermissionCodes.SupportReply)
                        || candidate.PermissionCodes.Contains(PermissionCodes.SupportManage)))) continue;
            if (await CandidateCanReadAsync(candidate, supportCase, ct)) eligible.Add(candidate);
        }
        return eligible;
    }

    public async Task<bool> CandidateCanManageCaseAsync(SupportOperatorCandidate candidate, SupportCase supportCase, CancellationToken ct)
        => candidate.Roles.Contains(AppRoles.SuperAdmin)
            || (candidate.UserType == UserType.Admin
                && candidate.PermissionCodes.Contains(PermissionCodes.SupportView)
                && candidate.PermissionCodes.Contains(PermissionCodes.SupportManage)
                && await CandidateCanReadAsync(candidate, supportCase, ct));

    private async Task<bool> CandidateCanReadAsync(SupportOperatorCandidate candidate, SupportCase supportCase, CancellationToken ct)
    {
        if (candidate.Roles.Contains(AppRoles.SuperAdmin)) return true;
        var scopes = candidate.Scopes.Select(scope => new AccessScope
        {
            Id = scope.Id, ScopeType = scope.ScopeType, CountryId = scope.CountryId, RegionId = scope.RegionId,
            RestaurantId = scope.RestaurantId, EntityId = scope.EntityId, EntityType = scope.EntityType,
            TenantId = scope.TenantId, IsActive = true, IsDeleted = false,
        }).ToList();
        if (HasUnconstrainedGlobalScope(scopes)) return true;
        if (supportCase.RestaurantId is { } restaurantId)
        {
            var restaurantIds = await GetScopedRestaurantIdsAsync(scopes, ct);
            return restaurantIds.Contains(restaurantId);
        }
        if (supportCase.TriageCountryId is not { } countryId) return false;
        var tenantIds = await db.Tenants.AsNoTracking().Where(x => x.CountryId == countryId && x.IsActive && !x.IsDeleted)
            .Select(x => x.Id).ToArrayAsync(ct);
        if (tenantIds.Length == 0 || !await db.Countries.AsNoTracking().AnyAsync(x => x.Id == countryId && x.IsActive && !x.IsDeleted, ct)) return false;
        var regions = await db.Set<Region>().AsNoTracking().Where(x => x.CountryId == countryId && x.IsActive && !x.IsDeleted)
            .Select(x => new { x.Id, x.ParentRegionId }).ToListAsync(ct);
        var ancestors = new HashSet<Guid>();
        var current = supportCase.TriageRegionId;
        while (current is { } regionId && ancestors.Add(regionId))
        {
            var region = regions.FirstOrDefault(x => x.Id == regionId);
            if (region is null) return false;
            current = region.ParentRegionId;
        }
        foreach (var scope in scopes)
        {
            if (tenantIds.Length > 1 && IsTenantConstrained(scope)) continue;
            var grant = MealMate.Application.Features.AdminCoverage.CoverageScopeGrant.TryCreate(scope, tenantIds.Length == 1 ? tenantIds[0] : Guid.Empty, countryId);
            if (grant is null || grant.RestaurantId is not null) continue;
            if (grant.IsCountryWide) return true;
            if (grant.RegionId is { } scopeRegion && supportCase.TriageRegionId.HasValue && ancestors.Contains(scopeRegion)) return true;
        }
        return false;
    }

    private static bool HasUnconstrainedGlobalScope(IEnumerable<AccessScope> scopes)
        => scopes.Any(x => x.ScopeType == AccessScopeType.Global && x.CountryId == null && x.RegionId == null
            && x.RestaurantId == null && x.EntityId == null && x.TenantId == null && string.IsNullOrWhiteSpace(x.EntityType));

    public async Task<bool> CanReadRestaurantAsync(Guid? restaurantId, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || ActorId is not { } actorId) return false;
        var candidate = (await db.GetActiveSupportOperatorCandidatesAsync(ct)).SingleOrDefault(x => x.UserId == actorId);
        if (candidate is null) return false;
        if (candidate.Roles.Contains(AppRoles.SuperAdmin)) return true;
        if (candidate.UserType != UserType.Admin || !candidate.PermissionCodes.Contains(PermissionCodes.SupportView)) return false;
        var scopes = candidate.Scopes.Select(scope => new AccessScope
        {
            Id = scope.Id, ScopeType = scope.ScopeType, CountryId = scope.CountryId, RegionId = scope.RegionId,
            RestaurantId = scope.RestaurantId, EntityId = scope.EntityId, EntityType = scope.EntityType,
            TenantId = scope.TenantId, IsActive = true, IsDeleted = false,
        }).ToList();
        return await CanReadRestaurantInAdminScopeAsync(restaurantId, ct, (scopes.Select(x => x.Id).ToArray(), scopes));
    }

    public async Task<(bool IsGlobal, Guid[] RestaurantIds)> GetOperationalRestaurantScopeAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || ActorId is not { } actorId) return (false, []);
        var candidate = (await db.GetActiveSupportOperatorCandidatesAsync(ct)).SingleOrDefault(x => x.UserId == actorId);
        if (candidate is null) return (false, []);
        if (candidate.Roles.Contains(AppRoles.SuperAdmin)) return (true, []);
        if (candidate.UserType != UserType.Admin || !candidate.PermissionCodes.Contains(PermissionCodes.SupportView)) return (false, []);
        var scopes = candidate.Scopes.Select(scope => new AccessScope
        {
            Id = scope.Id, ScopeType = scope.ScopeType, CountryId = scope.CountryId, RegionId = scope.RegionId,
            RestaurantId = scope.RestaurantId, EntityId = scope.EntityId, EntityType = scope.EntityType,
            TenantId = scope.TenantId, IsActive = true, IsDeleted = false,
        }).ToList();
        var global = HasUnconstrainedGlobalScope(scopes);
        if (global) return (true, []);
        var restaurants = await GetScopedRestaurantIdsAsync(scopes, ct);
        return (false, restaurants.ToArray());
    }

    public bool CanCreate(SupportCaseChannel channel) => currentUser.UserType switch
    {
        UserType.Customer => channel == SupportCaseChannel.Customer,
        UserType.Driver => channel == SupportCaseChannel.Driver,
        UserType.Restaurant => channel == SupportCaseChannel.Restaurant && currentUser.RestaurantId.HasValue,
        UserType.DeliveryManager => channel == SupportCaseChannel.DeliveryManager,
        UserType.Admin => IsSuperAdmin,
        _ => false,
    };

    public async Task<bool> CanReadAsync(SupportCase supportCase, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || !ActorId.HasValue) return false;
        if (currentUser.UserType == UserType.Driver && !await HasActiveDriverIdentityAsync(ct)) return false;
        if (IsSupportAdmin || IsSuperAdmin) return await IsWithinAdminScopeAsync(supportCase, ct);
        if (supportCase.RequesterUserId == ActorId.Value) return true;

        if (currentUser.UserType == UserType.Restaurant && currentUser.RestaurantId is { } restaurantId)
        {
            if (supportCase.Channel == SupportCaseChannel.Restaurant && supportCase.RestaurantId == restaurantId) return true;
            return await db.Set<SupportCaseTask>().AnyAsync(t => t.SupportCaseId == supportCase.Id
                && t.RestaurantId == restaurantId && t.RecipientType == SupportCaseTaskRecipientType.Restaurant
                && t.Status != SupportCaseTaskStatus.Cancelled, ct);
        }

        if (currentUser.UserType == UserType.DeliveryManager)
        {
            if (supportCase.RequesterUserId == ActorId.Value) return true;
            if (currentUser.RestaurantId is not { } managerRestaurantId
                || !await IsCurrentDeliveryManagerForRestaurantAsync(managerRestaurantId, ct)) return false;
            return await db.Set<SupportCaseTask>().AnyAsync(t => t.SupportCaseId == supportCase.Id
                && t.RecipientType == SupportCaseTaskRecipientType.DeliveryManager
                && t.RecipientUserId == ActorId.Value && t.RestaurantId == managerRestaurantId
                && t.Status != SupportCaseTaskStatus.Cancelled, ct);
        }

        return false;
    }

    public bool CanReadInternal => CanWriteInternal;

    public async Task<IQueryable<SupportCase>> VisibleCasesAsync(CancellationToken ct)
    {
        var actorId = ActorId ?? Guid.Empty;
        if (currentUser.UserType == UserType.Driver && !await HasActiveDriverIdentityAsync(ct))
            return db.Set<SupportCase>().Where(_ => false);
        if (IsSuperAdmin || IsSupportAdmin)
        {
            var candidates = await db.GetActiveSupportOperatorCandidatesAsync(ct);
            var candidate = candidates.SingleOrDefault(x => x.UserId == actorId);
            if (candidate is null || (!candidate.Roles.Contains(AppRoles.SuperAdmin)
                && (candidate.UserType != UserType.Admin || !candidate.PermissionCodes.Contains(PermissionCodes.SupportView))))
                return db.Set<SupportCase>().Where(_ => false);
            if (candidate.Roles.Contains(AppRoles.SuperAdmin)) return db.Set<SupportCase>().AsQueryable();
            var (restaurantIds, countryIds, regionIds, global) = await GetCandidateVisibleScopeAsync(candidate, ct);
            if (global) return db.Set<SupportCase>().AsQueryable();
            return db.Set<SupportCase>().Where(c => c.RestaurantId.HasValue && restaurantIds.Contains(c.RestaurantId.Value)
                || !c.RestaurantId.HasValue && (countryIds.Contains(c.TriageCountryId) || regionIds.Contains(c.TriageRegionId)));
        }
        if (currentUser.UserType == UserType.Restaurant && currentUser.RestaurantId is { } restaurantId)
        {
            return db.Set<SupportCase>().Where(c =>
                (c.Channel == SupportCaseChannel.Restaurant && c.RestaurantId == restaurantId)
                || db.Set<SupportCaseTask>().Any(t => t.SupportCaseId == c.Id && t.RestaurantId == restaurantId
                    && t.RecipientType == SupportCaseTaskRecipientType.Restaurant && t.Status != SupportCaseTaskStatus.Cancelled));
        }
        if (currentUser.UserType == UserType.DeliveryManager)
        {
            if (currentUser.RestaurantId is not { } managerRestaurantId
                || !await IsCurrentDeliveryManagerForRestaurantAsync(managerRestaurantId, ct))
                return db.Set<SupportCase>().Where(c => c.RequesterUserId == actorId);
            return db.Set<SupportCase>().Where(c => c.RequesterUserId == actorId
                || db.Set<SupportCaseTask>().Any(t => t.SupportCaseId == c.Id && t.RecipientUserId == actorId
                    && t.RecipientType == SupportCaseTaskRecipientType.DeliveryManager && t.RestaurantId == managerRestaurantId
                    && t.Status != SupportCaseTaskStatus.Cancelled));
        }
        return db.Set<SupportCase>().Where(c => c.RequesterUserId == actorId);
    }

    private async Task<(Guid[] RestaurantIds, Guid?[] CountryIds, Guid?[] RegionIds, bool Global)> GetCandidateVisibleScopeAsync(
        SupportOperatorCandidate candidate, CancellationToken ct)
    {
        var scopes = candidate.Scopes.Select(scope => new AccessScope
        {
            Id = scope.Id, ScopeType = scope.ScopeType, CountryId = scope.CountryId, RegionId = scope.RegionId,
            RestaurantId = scope.RestaurantId, EntityId = scope.EntityId, EntityType = scope.EntityType,
            TenantId = scope.TenantId, IsActive = true, IsDeleted = false,
        }).ToList();
        if (HasUnconstrainedGlobalScope(scopes)) return ([], [], [], true);
        var restaurants = (await GetScopedRestaurantIdsAsync(scopes, ct)).ToArray();
        var countries = await db.Countries.AsNoTracking().Where(x => x.IsActive && !x.IsDeleted)
            .Select(x => x.Id).ToArrayAsync(ct);
        var tenants = await db.Tenants.AsNoTracking().Where(x => x.IsActive && !x.IsDeleted && countries.Contains(x.CountryId))
            .Select(x => new { x.Id, x.CountryId }).ToListAsync(ct);
        var regions = await db.Set<Region>().AsNoTracking().Where(x => x.IsActive && !x.IsDeleted && countries.Contains(x.CountryId))
            .Select(x => new { x.Id, x.CountryId, x.ParentRegionId }).ToListAsync(ct);
        var coveredCountries = new HashSet<Guid>();
        var coveredRegions = new HashSet<Guid>();
        foreach (var countryId in countries)
        {
            var tenantIds = tenants.Where(x => x.CountryId == countryId).Select(x => x.Id).ToArray();
            if (tenantIds.Length == 0) continue;
            var eligibleScopes = tenantIds.Length == 1 ? scopes : scopes.Where(scope => !IsTenantConstrained(scope));
            var tenantId = tenantIds.Length == 1 ? tenantIds[0] : Guid.Empty;
            var grants = eligibleScopes.Select(scope => MealMate.Application.Features.AdminCoverage.CoverageScopeGrant.TryCreate(scope, tenantId, countryId))
                .Where(grant => grant is not null && grant.RestaurantId is null)
                .Cast<MealMate.Application.Features.AdminCoverage.CoverageScopeGrant>();
            foreach (var grant in grants)
            {
                if (grant.IsCountryWide) coveredCountries.Add(countryId);
                if (grant.RegionId is not { } root) continue;
                foreach (var region in regions.Where(x => x.CountryId == countryId))
                {
                    var seen = new HashSet<Guid>();
                    Guid? parent = region.Id;
                    while (parent is { } current && seen.Add(current))
                    {
                        if (current == root) { coveredRegions.Add(region.Id); break; }
                        parent = regions.FirstOrDefault(x => x.Id == current && x.CountryId == countryId)?.ParentRegionId;
                    }
                }
            }
        }
        return (restaurants, coveredCountries.Select(x => (Guid?)x).ToArray(), coveredRegions.Select(x => (Guid?)x).ToArray(), false);
    }

    private async Task<bool> IsWithinAdminScopeAsync(SupportCase supportCase, CancellationToken ct)
    {
        var candidates = await db.GetActiveSupportOperatorCandidatesAsync(ct);
        var candidate = candidates.SingleOrDefault(x => x.UserId == ActorId);
        return candidate is not null && (candidate.Roles.Contains(AppRoles.SuperAdmin)
            || candidate.UserType == UserType.Admin && candidate.PermissionCodes.Contains(PermissionCodes.SupportView)
                && await CandidateCanReadAsync(candidate, supportCase, ct));
    }

    public async Task<bool> IsCurrentDeliveryManagerForRestaurantAsync(Guid restaurantId, CancellationToken ct)
    {
        if (currentUser.UserType != UserType.DeliveryManager || ActorId is not { } actorId
            || currentUser.RestaurantId != restaurantId) return false;
        var currentManagers = await db.GetDeliveryManagerUserIdsAsync(restaurantId, ct);
        return currentManagers.Contains(actorId);
    }

    private async Task<bool> CanReadRestaurantInAdminScopeAsync(Guid? restaurantId, CancellationToken ct,
        (Guid[] Ids, List<AccessScope> Scopes)? scopes = null)
    {
        var scopeData = scopes ?? await GetScopesAsync(ct);
        if (scopeData.Scopes.Any(x => x.ScopeType == AccessScopeType.Global && x.CountryId == null && x.RegionId == null
            && x.RestaurantId == null && x.EntityId == null && x.TenantId == null && string.IsNullOrWhiteSpace(x.EntityType))) return true;
        if (restaurantId is not { } id) return false;
        var scoped = await GetScopedRestaurantIdsAsync(scopeData.Scopes, ct);
        return scoped.Contains(id);
    }

    private async Task<(Guid[] Ids, List<AccessScope> Scopes)> GetScopesAsync(CancellationToken ct)
    {
        var ids = currentUser.AccessScopeIds.Distinct().ToArray();
        var actor = ActorId;
        if (ids.Length == 0 || actor is null) return ([], []);
        var memberships = await db.UserScopes.AsNoTracking().Where(x => x.UserId == actor && ids.Contains(x.AccessScopeId))
            .Select(x => x.AccessScopeId).Distinct().ToArrayAsync(ct);
        if (memberships.Length != ids.Length) return (ids, []);
        var scopes = await db.AccessScopes.AsNoTracking().Where(x => ids.Contains(x.Id) && x.IsActive && !x.IsDeleted).ToListAsync(ct);
        return (ids, scopes.Count == ids.Length ? scopes : []);
    }

    private async Task<HashSet<Guid>> GetScopedRestaurantIdsAsync(IReadOnlyCollection<AccessScope> scopes, CancellationToken ct)
    {
        if (scopes.Count == 0) return [];
        var profiles = await db.RestaurantProfiles.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CountryId != null)
            .Select(x => new { x.Id, x.CountryId, x.PrimaryRegionId }).ToListAsync(ct);
        var countryIds = profiles.Select(x => x.CountryId!.Value).Distinct().ToArray();
        var tenants = await db.Tenants.AsNoTracking().Where(x => countryIds.Contains(x.CountryId) && x.IsActive && !x.IsDeleted)
            .Select(x => new { x.Id, x.CountryId }).ToListAsync(ct);
        var countries = await db.Countries.AsNoTracking().Where(x => countryIds.Contains(x.Id) && x.IsActive && !x.IsDeleted).Select(x => x.Id).ToArrayAsync(ct);
        var validProfiles = profiles.Where(p => countries.Contains(p.CountryId!.Value)
            && tenants.Any(t => t.CountryId == p.CountryId)).ToArray();
        var regions = await db.Set<Region>().AsNoTracking().Where(x => countryIds.Contains(x.CountryId) && x.IsActive && !x.IsDeleted)
            .Select(x => new { x.Id, x.CountryId, x.ParentRegionId }).ToListAsync(ct);
        var result = new HashSet<Guid>();
        foreach (var profile in validProfiles)
        {
            var countryId = profile.CountryId!.Value;
            var tenantIds = tenants.Where(x => x.CountryId == countryId).Select(x => x.Id).ToArray();
            if (tenantIds.Length == 0) continue;
            var tenantId = tenantIds.Length == 1 ? tenantIds[0] : Guid.Empty;
            var eligibleScopes = tenantIds.Length == 1 ? scopes : scopes.Where(scope => !IsTenantConstrained(scope)).ToArray();
            var regionPath = new HashSet<Guid>();
            var current = profile.PrimaryRegionId;
            while (current.HasValue && regionPath.Add(current.Value))
            {
                var region = regions.FirstOrDefault(x => x.Id == current.Value && x.CountryId == countryId);
                if (region is null) { regionPath.Clear(); break; }
                current = region.ParentRegionId;
            }
            if (eligibleScopes.Any(scope => scope.ScopeType == AccessScopeType.Global && scope.CountryId == null && scope.TenantId == null
                    && scope.RegionId == null && scope.RestaurantId == null && scope.EntityId == null && string.IsNullOrWhiteSpace(scope.EntityType)
                || MealMate.Application.Features.AdminCoverage.CoverageScopeGrant.TryCreate(scope, tenantId, countryId) is { } grant
                    && (grant.RestaurantId is null || grant.RestaurantId == profile.Id)
                    && (grant.RegionId is null || regionPath.Contains(grant.RegionId.Value))
                    && (grant.RestaurantId is not null || grant.RegionId is not null || grant.IsCountryWide)))
                result.Add(profile.Id);
        }
        return result;
    }

    private static bool IsTenantConstrained(AccessScope scope)
        => scope.TenantId.HasValue || scope.ScopeType == AccessScopeType.Tenant
            || string.Equals(scope.EntityType?.Trim(), nameof(Tenant), StringComparison.Ordinal);
}
