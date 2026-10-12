using MealMate.Domain.Common;

namespace MealMate.Domain.CustomerFinance;

public sealed class SubscriptionCancellation : AuditableEntity
{
    public Guid SubscriptionId { get; set; }

    public Guid CustomerUserId { get; set; }

    public int CountedDays { get; set; }

    public int UnusedDays { get; set; }

    public string Status { get; set; } = default!;

    public DateTime RequestedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    // Nullable for legacy rows; only new evidenced family settlements use this identity.
    public string? SettlementKind { get; set; }
    public Guid? FamilyGroupId { get; set; }
    public Guid? BatchId { get; set; }
    public Guid? BatchLineId { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? PayerCustomerUserId { get; set; }
    public string? ContributionRefundsJson { get; set; }
    public string? ResultMetadataJson { get; set; }

    public ICollection<RefundCalculation> RefundCalculations { get; set; } = new List<RefundCalculation>();

    public ICollection<CancellationFeeRecord> FeeRecords { get; set; } = new List<CancellationFeeRecord>();
}
