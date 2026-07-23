using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class MealRating : AuditableEntity
{
    public Guid MealId { get; set; }

    public Meal Meal { get; set; } = default!;

    public Guid CustomerUserId { get; set; }

    public Guid? SubscriptionDayId { get; set; }

    public int Score { get; set; }

    public string? Comment { get; set; }
}
