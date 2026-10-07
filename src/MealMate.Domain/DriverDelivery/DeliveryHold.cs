using MealMate.Domain.Common;

namespace MealMate.Domain.DriverDelivery;

public sealed class DeliveryHold : AuditableEntity
{
    public Guid? OrderId { get; set; }

    public Guid? SubscriptionDayId { get; set; }

    public Guid? TripId { get; set; }

    public Trip? Trip { get; set; }

    public string Reason { get; set; } = default!;

    public DateTime HeldAtUtc { get; set; }

    public DateTime? ReleasedAtUtc { get; set; }

    public string Status { get; set; } = default!;

    // ── v2 contact-flow columns (T07; additive) ──

    /// <summary>The stop this hold applies to (legacy rows keep null and are detected by backfill).</summary>
    public Guid? TripStopId { get; set; }

    public Guid? ContactCaseId { get; set; }

    /// <summary>Authenticated actor who created the hold.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Required free note (5–500 chars) recorded with the hold.</summary>
    public string? Notes { get; set; }

    public Guid? FirstAttemptCallId { get; set; }

    /// <summary>Set by the explicit resume action; never inferred from time alone.</summary>
    public DateTime? ResumedAtUtc { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }

    /// <summary>Supervisor resolution reason when closed via the resolve action.</summary>
    public string? ResolutionReason { get; set; }
}
