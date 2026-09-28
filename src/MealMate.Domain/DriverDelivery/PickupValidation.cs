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

    public bool IsActive(DateTime utcNow) => ConsumedAtUtc == null && ExpiresAtUtc > utcNow;
    public bool IsConsumed => ConsumedAtUtc != null;
    public bool IsExpired(DateTime utcNow) => ConsumedAtUtc == null && ExpiresAtUtc <= utcNow;

    public Result Consume(DateTime utcNow)
    {
        if (IsConsumed)
        {
            return Result.Failure("VALIDATION_EXPIRED_OR_CONSUMED: Validation token has already been consumed.");
        }

        if (ExpiresAtUtc <= utcNow)
        {
            return Result.Failure("VALIDATION_EXPIRED_OR_CONSUMED: Validation token has expired.");
        }

        ConsumedAtUtc = utcNow;
        return Result.Success();
    }
}
