using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.RestaurantOperations;

public sealed class MealBarcode : AuditableEntity
{
    public Guid SubscriptionDayId { get; set; }

    public Guid? OrderId { get; set; }

    public string BarcodeValue { get; set; } = default!;

    public DateTime GeneratedAtUtc { get; set; }

    public MealBarcodeStatus Status { get; set; } = MealBarcodeStatus.Generated;

    public Guid? TripStopId { get; set; }

    public DateTime? PrintedAtUtc { get; set; }

    public DateTime? ScannedAtUtc { get; set; }

    public bool IsRevoked { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public string? RevokedBy { get; set; }

    public string? RevocationReason { get; set; }

    public Guid? ReissuedBarcodeId { get; set; }

    public bool CanTransitionTo(MealBarcodeStatus target)
    {
        if (Status == target) return true;

        return Status switch
        {
            MealBarcodeStatus.Generated => target is MealBarcodeStatus.Printed
                                                 or MealBarcodeStatus.Revoked,
            MealBarcodeStatus.Printed => target is MealBarcodeStatus.Scanned
                                               or MealBarcodeStatus.Revoked,
            MealBarcodeStatus.Scanned => target is MealBarcodeStatus.Archived,
            MealBarcodeStatus.Archived => false,
            MealBarcodeStatus.Revoked => false,
            _ => false
        };
    }

    public Result TransitionTo(MealBarcodeStatus target)
    {
        if (!CanTransitionTo(target))
        {
            return Result.Failure($"INVALID_BARCODE_TRANSITION: Cannot transition barcode from {Status} to {target}.");
        }

        Status = target;
        return Result.Success();
    }
}
