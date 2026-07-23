using MealMate.Domain.Common;

namespace MealMate.Domain.DriverDelivery;

public sealed class DriverAssignment : AuditableEntity
{
    public Guid TripId { get; set; }

    public Trip Trip { get; set; } = default!;

    public Guid DriverProfileId { get; set; }

    public DateTime AssignedAtUtc { get; set; }

    public string Status { get; set; } = default!;
}
