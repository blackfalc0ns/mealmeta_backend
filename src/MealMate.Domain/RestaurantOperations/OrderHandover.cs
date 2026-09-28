using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class OrderHandover : AuditableEntity
{
    public Guid DailyOrderId { get; set; }

    public RestaurantDailyOrder DailyOrder { get; set; } = default!;

    public Guid? TripId { get; set; }

    public Guid? TripStopId { get; set; }

    public Guid? MealBarcodeId { get; set; }

    public Guid? DriverProfileId { get; set; }

    public int AttemptNumber { get; set; } = 1;

    public Guid? SupersedesHandoverId { get; set; }

    public DateTime? HandedOverAtUtc { get; set; }

    public string Status { get; set; } = default!;

    public DateTime? RevokedAtUtc { get; set; }

    public string? RevokedBy { get; set; }

    public string? RevocationReason { get; set; }

    public ICollection<OrderHandoverRevocation> Revocations { get; set; } = new List<OrderHandoverRevocation>();
}
