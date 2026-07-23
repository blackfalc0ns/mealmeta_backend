using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class RestaurantServiceRegion : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public Guid RegionId { get; set; }

    public bool IsActive { get; set; } = true;
}
