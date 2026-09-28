using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class MealBarcode : AuditableEntity
{
    public Guid SubscriptionDayId { get; set; }

    public Guid? OrderId { get; set; }

    public string BarcodeValue { get; set; } = default!;

    public DateTime GeneratedAtUtc { get; set; }

    public string Status { get; set; } = "Generated";

    public Guid? TripStopId { get; set; }

    public DateTime? PrintedAtUtc { get; set; }

    public DateTime? ScannedAtUtc { get; set; }

    public bool IsRevoked { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public string? RevokedBy { get; set; }

    public string? RevocationReason { get; set; }

    public Guid? ReissuedBarcodeId { get; set; }
}
