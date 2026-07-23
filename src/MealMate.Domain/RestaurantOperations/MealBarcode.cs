using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class MealBarcode : AuditableEntity
{
    public Guid SubscriptionDayId { get; set; }

    public Guid? OrderId { get; set; }

    public string BarcodeValue { get; set; } = default!;

    public DateTime GeneratedAtUtc { get; set; }
}
