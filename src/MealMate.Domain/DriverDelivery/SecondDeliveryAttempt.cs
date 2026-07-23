using MealMate.Domain.Common;

namespace MealMate.Domain.DriverDelivery;

public sealed class SecondDeliveryAttempt : AuditableEntity
{
    public Guid? OrderId { get; set; }

    public Guid? SubscriptionDayId { get; set; }

    public int AttemptNumber { get; set; }

    public DateTime ScheduledAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? Outcome { get; set; }
}
