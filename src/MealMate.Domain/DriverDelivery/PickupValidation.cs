using MealMate.Domain.Common;

namespace MealMate.Domain.DriverDelivery;

public sealed class PickupValidation : BaseEntity
{
    public Guid TripId { get; set; }
    public Guid BoxId { get; set; }
    public Guid DriverProfileId { get; set; }
    public Guid DriverAssignmentId { get; set; }
    public string BarcodeValue { get; set; } = default!;
    public string TokenHash { get; set; } = default!;
    public DateTime ValidatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public bool IsManualEntry { get; set; }
}
