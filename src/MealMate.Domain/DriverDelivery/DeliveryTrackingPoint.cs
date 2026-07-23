using MealMate.Domain.Common;

namespace MealMate.Domain.DriverDelivery;

public sealed class DeliveryTrackingPoint : BaseEntity
{
    public Guid TripId { get; set; }

    public Trip Trip { get; set; } = default!;

    public Guid DriverProfileId { get; set; }

    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }

    public DateTime RecordedAtUtc { get; set; }
}
