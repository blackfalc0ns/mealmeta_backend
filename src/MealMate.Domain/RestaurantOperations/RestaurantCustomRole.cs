using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

/// <summary>
/// A custom (non-system) role created by a restaurant owner.
/// RoleId is prefixed with "custom_" to distinguish from system roles.
/// </summary>
public sealed class RestaurantCustomRole : AuditableEntity
{
    public Guid RestaurantRegistrationId { get; set; }

    /// <summary>Auto-generated unique roleId, e.g. "custom_a3f9..."</summary>
    public string RoleId { get; set; } = default!;

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public string DescriptionAr { get; set; } = string.Empty;

    public string DescriptionEn { get; set; } = string.Empty;
}
