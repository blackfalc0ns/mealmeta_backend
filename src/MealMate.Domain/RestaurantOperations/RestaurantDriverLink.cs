using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class RestaurantDriverLink : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public Guid DriverProfileId { get; set; }

    public bool IsActive { get; set; } = true;
}
