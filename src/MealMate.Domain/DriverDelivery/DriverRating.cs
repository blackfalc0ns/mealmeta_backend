using MealMate.Domain.Common;

namespace MealMate.Domain.DriverDelivery;

public sealed class DriverRating : AuditableEntity
{
    public Guid DriverProfileId { get; set; }

    public Guid CustomerUserId { get; set; }

    public Guid? SubscriptionDayId { get; set; }

    public int Score { get; set; }

    public string? Comment { get; set; }
}
