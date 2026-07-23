using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.RestaurantOperations;

public sealed class DailyCapacity : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public DateOnly ServiceDate { get; set; }

    public CapacityType CapacityType { get; set; } = CapacityType.Base;

    public int MaxBoxes { get; set; }

    public int ReservedBoxes { get; set; }

    public Guid? CampaignId { get; set; }
}
