using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Catalog;

/// <summary>
/// Resolves the single operating tenant allowed to use the Kuwait catalog.
/// Tenant identity always comes from authenticated claims; request payloads cannot select it.
/// </summary>
public sealed class KuwaitTenantResolver
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public KuwaitTenantResolver(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public Task<Result<Tenant>> ResolveAsync(CancellationToken cancellationToken)
        => ResolveAsync(allowSuperAdminFallback: false, cancellationToken);

    /// <summary>
    /// Resolves the catalog tenant for the public onboarding read path. The catalog
    /// is needed before account authentication exists, so anonymous/read-only access
    /// uses the canonical Kuwait tenant. Protected mutations never use this method.
    /// </summary>
    public async Task<Result<Tenant>> ResolveCatalogReadAsync(CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantIds.Count == 0)
        {
            var canonical = await GetActiveKuwaitTenantAsync("TEN-KW", cancellationToken);
            return canonical is null
                ? Result.Failure<Tenant>(Error.NotFound("catalog.tenant_not_found"))
                : Result.Success(canonical);
        }

        return await ResolveAsync(cancellationToken);
    }

    public async Task<Result<Tenant>> ResolveAsync(bool allowSuperAdminFallback, CancellationToken cancellationToken)
    {
        var ids = _currentUser.TenantIds.Distinct().ToList();
        if (ids.Count == 0 && allowSuperAdminFallback && _currentUser.IsInRole(AppRoles.SuperAdmin))
        {
            var seededTenant = await GetActiveKuwaitTenantAsync("TEN-KW", cancellationToken);
            return seededTenant is null
                ? Result.Failure<Tenant>(Error.NotFound("catalog.tenant_not_found"))
                : Result.Success(seededTenant);
        }

        if (ids.Count != 1)
        {
            return Result.Failure<Tenant>(ids.Count == 0
                ? Error.Unauthorized("catalog.tenant_required")
                : Error.Conflict("catalog.single_tenant_required"));
        }

        var tenant = await _db.Tenants
            .Include(x => x.Country)
            .FirstOrDefaultAsync(x => ids.Contains(x.Id)
                                      && x.IsActive
                                      && x.Status == TenantStatus.Active
                                      && x.Country.IsActive,
                cancellationToken);

        if (tenant is null)
            return Result.Failure<Tenant>(Error.Forbidden("catalog.tenant_invalid"));

        if (!string.Equals(tenant.Code, "TEN-KW", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(tenant.Country.Code, "KW", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<Tenant>(Error.Forbidden("catalog.tenant_not_kuwait"));
        }

        return Result.Success(tenant);
    }

    private Task<Tenant?> GetActiveKuwaitTenantAsync(string code, CancellationToken cancellationToken)
        => _db.Tenants
            .Include(x => x.Country)
            .FirstOrDefaultAsync(x => x.Code == code
                                      && x.IsActive
                                      && x.Status == TenantStatus.Active
                                      && x.Country.Code == "KW"
                                      && x.Country.IsActive,
                cancellationToken);
}
