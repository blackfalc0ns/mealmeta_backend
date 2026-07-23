using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class MealLabel : AuditableEntity
{
    public Guid SubscriptionDayId { get; set; }

    public string LabelPayloadJson { get; set; } = "{}";

    public DateTime GeneratedAtUtc { get; set; }
}
