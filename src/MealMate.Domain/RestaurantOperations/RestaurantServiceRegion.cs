using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class RestaurantServiceRegion : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public Guid RegionId { get; set; }

    public bool IsActive { get; set; } = true;

    public TimeOnly DeliveryWindowStart { get; set; } = new TimeOnly(8, 0);

    public TimeOnly DeliveryWindowEnd { get; set; } = new TimeOnly(14, 0);

    public int? PreparationLeadTimeMinutes { get; set; }
}
