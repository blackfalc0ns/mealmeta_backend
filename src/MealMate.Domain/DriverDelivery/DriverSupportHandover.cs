using MealMate.Domain.Common;

namespace MealMate.Domain.DriverDelivery;

/// <summary>Persisted custody transfer request for a driver support issue.</summary>
public sealed class DriverSupportHandover : AuditableEntity
{
    public Guid ComplaintId { get; set; }
    public Guid RequestedIssueId { get; set; }
    public Guid TripId { get; set; }
    public Guid OriginalDriverProfileId { get; set; }
    public Guid ReplacementDriverProfileId { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public string IdempotencyKey { get; set; } = default!;
    public string Status { get; set; } = "Assigned";
    public string? Notes { get; set; }
    public decimal PickupLatitude { get; set; }
    public decimal PickupLongitude { get; set; }
    public string PickupLocationSource { get; set; } = default!;
    public DateTime? PickupLocationRecordedAtUtc { get; set; }
    public DateTime AssignedAtUtc { get; set; }
    public DateTime? ArrivedAtUtc { get; set; }
    public decimal? ArrivalLatitude { get; set; }
    public decimal? ArrivalLongitude { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public Guid? ReplacementTripId { get; set; }
    public ICollection<DriverSupportHandoverBox> Boxes { get; set; } = new List<DriverSupportHandoverBox>();
}

/// <summary>Receipt evidence for one existing trip manifest stop.</summary>
public sealed class DriverSupportHandoverBox : AuditableEntity
{
    public Guid HandoverId { get; set; }
    public DriverSupportHandover Handover { get; set; } = default!;
    public Guid TripStopId { get; set; }
    public string ManifestCode { get; set; } = default!;
    public string Status { get; set; } = "Pending";
    public DateTime? ScannedAtUtc { get; set; }
    public string? ScanMethod { get; set; }
    public string? FollowUpNote { get; set; }
    public Guid? FollowUpId { get; set; }
    public DeliveryHold? FollowUp { get; set; }
}
