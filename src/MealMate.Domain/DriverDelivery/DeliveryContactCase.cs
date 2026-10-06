using MealMate.Domain.Common;

namespace MealMate.Domain.DriverDelivery;

/// <summary>
/// Contact-cycle state machine for one stop under one driver assignment (F14).
/// One open cycle per (stop, assignment); reassignment closes the old cycle and a
/// new driver starts with a clean one — no inherited reveal rights or counts.
/// </summary>
public sealed class DeliveryContactCase : AuditableEntity
{
    /// <summary>Cycle is open while the assignment is the stop's active one.</summary>
    public const string StateOpen = "Open";
    public const string StateClosed = "Closed";

    private DeliveryContactCase() { }

    public DeliveryContactCase(
        Guid tripStopId,
        Guid activeDriverAssignmentId,
        int contactCycleId,
        DateTime createdAtUtc)
    {
        if (tripStopId == Guid.Empty)
            throw new ArgumentException("TripStopId must not be empty.", nameof(tripStopId));
        if (activeDriverAssignmentId == Guid.Empty)
            throw new ArgumentException("ActiveDriverAssignmentId must not be empty.", nameof(activeDriverAssignmentId));

        TripStopId = tripStopId;
        ActiveDriverAssignmentId = activeDriverAssignmentId;
        ContactCycleId = contactCycleId;
        State = StateOpen;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid TripStopId { get; private set; }

    public Guid ActiveDriverAssignmentId { get; private set; }

    /// <summary>Monotonic cycle number scoped to (TripStopId, ActiveDriverAssignmentId).</summary>
    public int ContactCycleId { get; private set; }

    /// <summary>Open/Closed — string state per the legacy adapter convention.</summary>
    public string State { get; private set; } = StateOpen;

    /// <summary>Qualified no-answer attempts in THIS cycle only (technical failures never increment).</summary>
    public int QualifiedMissedCount { get; private set; }

    public Guid? HoldId { get; private set; }

    public Guid? LastCallId { get; private set; }

    /// <summary>Optimistic-concurrency version for supervisor resolution.</summary>
    public long Version { get; set; }

    public void RecordQualifiedMissedAttempt(Guid callId, DateTime utcNow)
    {
        if (State != StateOpen)
            throw new InvalidOperationException("Cannot record an attempt on a closed contact case.");

        QualifiedMissedCount++;
        LastCallId = callId;
        ModifiedAtUtc = utcNow;
    }

    public void AttachHold(Guid holdId, DateTime utcNow)
    {
        if (State != StateOpen)
            throw new InvalidOperationException("Cannot attach a hold to a closed contact case.");
        if (HoldId.HasValue)
            throw new InvalidOperationException("An open hold is already attached to this contact case.");

        HoldId = holdId;
        ModifiedAtUtc = utcNow;
    }

    public bool CanRevealPhone() =>
        State == StateOpen && QualifiedMissedCount >= 2 && HoldId.HasValue;

    public void Close(DateTime utcNow)
    {
        State = StateClosed;
        ModifiedAtUtc = utcNow;
    }
}
