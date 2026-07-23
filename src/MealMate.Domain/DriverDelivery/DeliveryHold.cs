using MealMate.Domain.Common;

namespace MealMate.Domain.DriverDelivery;

public sealed class DeliveryHold : AuditableEntity
{
    public Guid? OrderId { get; set; }

    public Guid? SubscriptionDayId { get; set; }

    public Guid? TripId { get; set; }

    public Trip? Trip { get; set; }

    public string Reason { get; set; } = default!;

    public DateTime HeldAtUtc { get; set; }

    public DateTime? ReleasedAtUtc { get; set; }

    public string Status { get; set; } = default!;
}
