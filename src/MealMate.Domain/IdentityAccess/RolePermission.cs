using MealMate.Domain.Common;

namespace MealMate.Domain.IdentityAccess;

public sealed class RolePermission : AuditableEntity
{
    public Guid RoleId { get; set; }

    public Guid PermissionId { get; set; }

    public Permission Permission { get; set; } = default!;
}
