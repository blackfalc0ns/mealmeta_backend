using MealMate.Domain.Common;

namespace MealMate.Domain.DriverDelivery;

public sealed class PickupConditionPhoto : BaseEntity
{
    public Guid PickupValidationId { get; set; }
    public Guid TripId { get; set; }
    public Guid BoxId { get; set; }
    public Guid DriverProfileId { get; set; }
    public string StorageKey { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long SizeBytes { get; set; }
    public string? Checksum { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
