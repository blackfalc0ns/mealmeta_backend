using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class BusyStatus : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public bool IsBusy { get; set; }

    public string? Reason { get; set; }

    public DateTime StartsAtUtc { get; set; }

    public DateTime? EndsAtUtc { get; set; }
}
