using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class RestaurantOperatingSchedule : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public DayOfWeek DayOfWeek { get; set; }

    public bool IsWorkingDay { get; set; } = true;
}
