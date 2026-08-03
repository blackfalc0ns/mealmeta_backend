using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class BranchStaffAssignment : AuditableEntity
{
    public Guid RestaurantBranchId { get; set; }

    public Guid UserId { get; set; }

    public string RoleId { get; set; } = default!;

    public bool IsBranchManager { get; set; }

    public bool IsActive { get; set; } = true;
}
