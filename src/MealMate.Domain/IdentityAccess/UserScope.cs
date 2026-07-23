using MealMate.Domain.Common;

namespace MealMate.Domain.IdentityAccess;

public sealed class UserScope : AuditableEntity
{
    public Guid UserId { get; set; }

    public Guid AccessScopeId { get; set; }

    public AccessScope AccessScope { get; set; } = default!;
}
