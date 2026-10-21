using MealMate.Domain.Common;

namespace MealMate.Domain.CustomerFinance;

public enum CarriedOverObligationReason
{
    FamilyMemberFreeze = 1,
    PlatformEmergencyFailure = 2,
    AllergenIncompatibility = 3,
}

public enum CarriedOverObligationState
{
    PendingSchedule = 1,
    GuaranteedHold = 2,
    Executed = 3,
    Refunded = 4,
    Cancelled = 5,
}

/// <summary>
/// F06.1 & Capacity Section 9/13/18:
/// An independent carried-over service commitment resulting from a frozen family member
/// or platform failure compensation that survives past the family subscription cycle's close
/// without extending the primary subscription end or delaying its renewal window.
/// </summary>
public sealed class CarriedOverServiceObligation : BaseEntity
{
    public Guid SubscriptionId { get; set; }
    public Guid BeneficiaryId { get; set; }
    public Guid OriginalDayId { get; set; }
    public DateOnly OriginalServiceDate { get; set; }
    public DateOnly? TargetDate { get; set; }
    public decimal LiabilityAmount { get; set; }
    public string Currency { get; set; } = "KWD";
    public CarriedOverObligationReason Reason { get; set; } = CarriedOverObligationReason.FamilyMemberFreeze;
    public CarriedOverObligationState State { get; set; } = CarriedOverObligationState.GuaranteedHold;
    public string? JournalRef { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExecutedAtUtc { get; set; }

    public void Execute(string? journalRef = null, DateTime? executedAtUtc = null)
    {
        if (State != CarriedOverObligationState.GuaranteedHold && State != CarriedOverObligationState.PendingSchedule)
            throw new InvalidOperationException($"Cannot execute obligation in state '{State}'.");
        State = CarriedOverObligationState.Executed;
        JournalRef = journalRef ?? JournalRef;
        ExecutedAtUtc = executedAtUtc ?? DateTime.UtcNow;
    }
}
