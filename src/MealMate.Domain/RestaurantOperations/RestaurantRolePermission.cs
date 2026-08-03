using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

/// <summary>
/// Stores the permission level for a specific role and module per restaurant.
/// RoleId: owner | branch_manager | kitchen | finance | viewer
/// ModuleKey: overview | orders | menu | operations | delivery | finance | quality | team | settings
/// PermissionLevel: none | view | manage
/// </summary>
public sealed class RestaurantRolePermission : AuditableEntity
{
    public Guid RestaurantRegistrationId { get; set; }

    public string RoleId { get; set; } = default!;

    public string ModuleKey { get; set; } = default!;

    public string PermissionLevel { get; set; } = "none";
}
