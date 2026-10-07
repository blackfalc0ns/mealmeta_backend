using MealMate.Domain.Common;

namespace MealMate.Domain.DriverDelivery;

public sealed class DeliveryTrackingPoint : BaseEntity
{
    public Guid TripId { get; set; }

    public Trip Trip { get; set; } = default!;

    public Guid DriverProfileId { get; set; }

    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }

    public double? Heading { get; set; }

    public double? SpeedKmh { get; set; }

    public DateTime RecordedAtUtc { get; set; }

    /// <summary>When the device captured the fix (server-set on ingest; never client-trusted beyond skew rules).</summary>
    public DateTime? CapturedAtUtc { get; set; }

    /// <summary>When the backend received the fix; age decisions use captured+received to prevent replay.</summary>
    public DateTime? ReceivedAtUtc { get; set; }

    /// <summary>Reported horizontal accuracy in meters; contact eligibility rejects fixes worse than the policy limit.</summary>
    public double? AccuracyMeters { get; set; }
}
