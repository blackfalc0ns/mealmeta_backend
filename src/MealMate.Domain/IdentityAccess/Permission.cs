using MealMate.Domain.Common;

namespace MealMate.Domain.IdentityAccess;

public sealed class Permission : AuditableEntity
{
    public string Code { get; set; } = default!;

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public string Module { get; set; } = default!;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
