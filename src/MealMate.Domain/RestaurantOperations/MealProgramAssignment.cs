using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class MealProgramAssignment : AuditableEntity
{
    public Guid MealId { get; set; }
    public Meal Meal { get; set; } = default!;
    public Guid ProgramId { get; set; }
    public Guid? BundleId { get; set; }
}
