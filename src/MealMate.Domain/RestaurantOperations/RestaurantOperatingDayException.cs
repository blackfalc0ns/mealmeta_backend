using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class RestaurantOperatingDayException : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public DateOnly Date { get; set; }

    public bool IsWorkingDay { get; set; }

    public string? Reason { get; set; }
}
