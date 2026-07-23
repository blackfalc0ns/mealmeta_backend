using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class OrderHandover : AuditableEntity
{
    public Guid DailyOrderId { get; set; }

    public RestaurantDailyOrder DailyOrder { get; set; } = default!;

    public Guid? TripId { get; set; }

    public Guid? DriverProfileId { get; set; }

    public DateTime? HandedOverAtUtc { get; set; }

    public string Status { get; set; } = default!;
}
