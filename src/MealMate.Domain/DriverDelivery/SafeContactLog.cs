using MealMate.Domain.Common;

namespace MealMate.Domain.DriverDelivery;

public sealed class SafeContactLog : BaseEntity
{
    public Guid? OrderId { get; set; }

    public Guid? SubscriptionDayId { get; set; }

    public Guid DriverProfileId { get; set; }

    public Guid CustomerUserId { get; set; }

    public string ContactType { get; set; } = default!;

    public bool Within3Km { get; set; }

    public DateTime OccurredAtUtc { get; set; }
}
