using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public enum CustomerCompensationStatus
{
    PendingChoice = 1,
    AlternativeDayChosen = 2,
    CashRefundChosen = 3,
    AutoRefundInitiated = 4,
    Settled = 5,
    Expired = 6
}

/// <summary>
/// Tracks customer service compensation resulting from operational deficits (e.g., kitchen capacity overload,
/// confirmation timeouts, unfulfilled deliveries).
/// Enforces:
/// 1. 48-hour customer choice window (CashRefund vs. AlternativeDay).
/// 2. Automatic cash refund fallback if deadline elapses without explicit choice.
/// 3. Clear breakdown of original funding sources (Card vs. Wallet vs. Apology Credit).
/// 4. Independent service day scheduling honoring the unified 72-hour lead time.
/// </summary>
public sealed class CustomerServiceCompensation : AuditableEntity
{
    public Guid CustomerUserId { get; set; }

    public Guid SubscriptionId { get; set; }

    public Guid? BeneficiaryFamilyMemberId { get; set; }

    public DateOnly FailedServiceDate { get; set; }

    public string Reason { get; set; } = "KitchenCapacityDeficit";

    public decimal GrossAmount { get; set; }

    public decimal DiscountPercent { get; set; }

    public decimal NetDayValue { get; set; }

    public string Currency { get; set; } = "KWD";

    public decimal CardRefundAmount { get; set; }

    public decimal WalletRefundAmount { get; set; }

    public decimal ApologyCreditAmount { get; set; }

    public DateTime FailureConfirmedAtUtc { get; set; }

    public DateTime ChoiceDeadlineUtc { get; set; }

    public DateTime RefundInitiationDeadlineUtc { get; set; }

    public CustomerCompensationStatus Status { get; set; } = CustomerCompensationStatus.PendingChoice;

    public bool EligibleForAlternativeDay { get; set; } = true;

    public DateOnly? SelectedAlternativeDate { get; set; }

    public Guid? AlternativeCommitmentId { get; set; }

    public DateTime? DecidedAtUtc { get; set; }

    public bool IsChoiceExpired(DateTime nowUtc) =>
        Status == CustomerCompensationStatus.PendingChoice && nowUtc >= ChoiceDeadlineUtc;
}
