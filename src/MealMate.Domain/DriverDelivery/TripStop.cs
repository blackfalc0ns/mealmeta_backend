using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;

namespace MealMate.Domain.DriverDelivery;

public sealed class TripStop : AuditableEntity
{
    public Guid TripId { get; set; }

    public Trip Trip { get; set; } = default!;

    public Guid? DailyOrderId { get; set; }

    public Guid? SubscriptionDayId { get; set; }

    public Guid? MealBarcodeId { get; set; }

    public MealBarcode? MealBarcode { get; set; }

    public string OrderCode { get; set; } = default!;

    public string CustomerMaskedId { get; set; } = default!;

    public string? CustomerName { get; set; }

    public string? CustomerPhone { get; set; }

    public string ZoneNameAr { get; set; } = default!;

    public string ZoneNameEn { get; set; } = default!;

    /// <summary>Snapshot of the customer's full delivery address line at assignment time (zone name lives in ZoneNameAr/En).</summary>
    public string? FullAddress { get; set; }

    public string MealSummaryAr { get; set; } = default!;

    public string MealSummaryEn { get; set; } = default!;

    public int BoxCount { get; set; } = 1;

    public string SlotLabel { get; set; } = default!;

    public int SequenceNumber { get; set; }

    public TripStopStatus Status { get; set; } = TripStopStatus.Pending;

    /// <summary>
    /// First confirmed driver arrival at the customer location (arrive-customer).
    /// Written once by the assigned driver and never overwritten: repeated arrive
    /// calls keep returning this same instant, so screen reloads stay stable.
    /// </summary>
    public DateTime? StartedDeliveryAtUtc { get; set; }
    public DateTime? ArrivedAtUtc { get; set; }

    public DateTime? DeliveredAtUtc { get; set; }

    public string? FailureReason { get; set; }
    
    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public bool CanTransitionTo(TripStopStatus target)
    {
        if (Status == target) return true;

        return Status switch
        {
            TripStopStatus.Pending => target is TripStopStatus.PickedUp
                                            or TripStopStatus.Failed,
            TripStopStatus.PickedUp => target is TripStopStatus.Delivered
                                             or TripStopStatus.Failed,
            TripStopStatus.Failed => target is TripStopStatus.ReturnedToKitchen,
            TripStopStatus.Delivered => false,
            TripStopStatus.ReturnedToKitchen => false,
            _ => false
        };
    }

    public Result TransitionTo(TripStopStatus target)
    {
        if (!CanTransitionTo(target))
        {
            return Result.Failure($"INVALID_TRIP_STOP_TRANSITION: Cannot transition trip stop from {Status} to {target}.");
        }

        Status = target;
        return Result.Success();
    }
}
