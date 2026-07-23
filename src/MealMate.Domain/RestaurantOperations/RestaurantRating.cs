using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class RestaurantRating : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public Guid CustomerUserId { get; set; }

    public Guid? SubscriptionDayId { get; set; }

    public int Score { get; set; }

    public string? Comment { get; set; }
}
