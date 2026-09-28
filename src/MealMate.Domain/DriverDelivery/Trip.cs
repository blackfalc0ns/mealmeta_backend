using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.DriverDelivery;

public sealed class Trip : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public Guid? DispatcherUserId { get; set; }

    public TripStatus Status { get; set; } = TripStatus.Draft;

    public string Code { get; set; } = default!;

    public string? ShiftName { get; set; }

    public DateOnly PlannedDate { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? Notes { get; set; }

    public ICollection<TripStop> Stops { get; set; } = new List<TripStop>();

    public ICollection<DriverAssignment> DriverAssignments { get; set; } = new List<DriverAssignment>();

    public ICollection<PickupScan> PickupScans { get; set; } = new List<PickupScan>();

    public ICollection<DeliveryTrackingPoint> TrackingPoints { get; set; } = new List<DeliveryTrackingPoint>();

    public bool CanTransitionTo(TripStatus target)
    {
        if (Status == target) return true;

        return Status switch
        {
            TripStatus.Draft => target is TripStatus.Planned
                                      or TripStatus.Cancelled,
            TripStatus.Planned => target is TripStatus.Assigned
                                        or TripStatus.Draft
                                        or TripStatus.Cancelled,
            TripStatus.Assigned => target is TripStatus.InProgress
                                         or TripStatus.Planned
                                         or TripStatus.Cancelled,
            TripStatus.InProgress => target is TripStatus.Completed
                                           or TripStatus.Cancelled,
            TripStatus.Completed => false,
            TripStatus.Cancelled => false,
            _ => false
        };
    }

    public Result TransitionTo(TripStatus target)
    {
        if (!CanTransitionTo(target))
        {
            return Result.Failure($"INVALID_TRIP_TRANSITION: Cannot transition trip from {Status} to {target}.");
        }

        Status = target;
        return Result.Success();
    }
}
