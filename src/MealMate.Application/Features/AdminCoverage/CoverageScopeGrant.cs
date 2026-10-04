using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;

namespace MealMate.Application.Features.AdminCoverage;

/// <summary>One active scope row. Constraints within a grant are conjunctive; rows are unioned.</summary>
public sealed record CoverageScopeGrant(bool IsCountryWide, Guid? RegionId, Guid? RestaurantId)
{
    public static CoverageScopeGrant? TryCreate(AccessScope scope, Guid tenantId, Guid countryId)
    {
        if (!scope.IsActive || scope.IsDeleted) return null;
        if (scope.CountryId is Guid scopeCountryId && scopeCountryId != countryId) return null;
        if (scope.TenantId is Guid scopeTenantId && scopeTenantId != tenantId) return null;

        var regionId = scope.RegionId;
        var restaurantId = scope.RestaurantId;
        var entityType = scope.EntityType?.Trim();
        if (scope.EntityId is not null || !string.IsNullOrWhiteSpace(entityType))
        {
            if (scope.EntityId is not Guid scopedEntityId || string.IsNullOrWhiteSpace(entityType)) return null;
            switch (entityType)
            {
                case nameof(Tenant):
                    if (scopedEntityId != tenantId || (scope.TenantId is Guid tenantConstraint && tenantConstraint != scopedEntityId)) return null;
                    break;
                case nameof(Country):
                    if (scopedEntityId != countryId || (scope.CountryId is Guid countryConstraint && countryConstraint != scopedEntityId)) return null;
                    break;
                case nameof(Region):
                    if (regionId is Guid existingRegion && existingRegion != scopedEntityId) return null;
                    regionId = scopedEntityId;
                    break;
                case nameof(RestaurantProfile):
                case "Restaurant":
                    if (restaurantId is Guid existingRestaurant && existingRestaurant != scopedEntityId) return null;
                    restaurantId = scopedEntityId;
                    break;
                default:
                    return null;
            }
        }

        switch (scope.ScopeType)
        {
            case AccessScopeType.Country:
                if (scope.CountryId != countryId) return null;
                break;
            case AccessScopeType.Tenant:
                if (scope.TenantId != tenantId && !(string.Equals(entityType, nameof(Tenant), StringComparison.Ordinal) && scope.EntityId == tenantId)) return null;
                break;
            case AccessScopeType.Region:
                if (regionId is null) return null;
                break;
            case AccessScopeType.Restaurant:
                if (restaurantId is null) return null;
                break;
            case AccessScopeType.Entity:
                if (entityType is not (nameof(Tenant) or nameof(Country) or nameof(Region) or nameof(RestaurantProfile) or "Restaurant")) return null;
                break;
            default:
                return null;
        }

        var entityCountryWide = scope.ScopeType == AccessScopeType.Entity
            && entityType is nameof(Tenant) or nameof(Country)
            && regionId is null
            && restaurantId is null;
        var fullCountry = (scope.ScopeType is AccessScopeType.Country or AccessScopeType.Tenant || entityCountryWide)
            && regionId is null
            && restaurantId is null;
        return new CoverageScopeGrant(fullCountry, regionId, restaurantId);
    }
}
