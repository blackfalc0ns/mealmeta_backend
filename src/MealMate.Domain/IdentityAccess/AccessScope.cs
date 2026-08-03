using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;

namespace MealMate.Domain.IdentityAccess;

public sealed class AccessScope : AuditableEntity
{
    public string Name { get; set; } = default!;

    public AccessScopeType ScopeType { get; set; }

    public Guid? CountryId { get; set; }

    public Guid? RegionId { get; set; }

    public Guid? RestaurantId { get; set; }

    public Guid? EntityId { get; set; }

    public string? EntityType { get; set; }

    public Guid? TenantId { get; set; }

    public Tenant? Tenant { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<UserScope> UserScopes { get; set; } = new List<UserScope>();
}
