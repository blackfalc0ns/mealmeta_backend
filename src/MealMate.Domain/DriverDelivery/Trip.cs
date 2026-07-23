using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.DriverDelivery;

public sealed class Trip : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public Guid? DispatcherUserId { get; set; }

    public TripStatus Status { get; set; } = TripStatus.Draft;

    public DateOnly PlannedDate { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? Notes { get; set; }

    public ICollection<DriverAssignment> DriverAssignments { get; set; } = new List<DriverAssignment>();

    public ICollection<PickupScan> PickupScans { get; set; } = new List<PickupScan>();

    public ICollection<DeliveryTrackingPoint> TrackingPoints { get; set; } = new List<DeliveryTrackingPoint>();
}
