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

    public ICollection<RefundCalculation> RefundCalculations { get; set; } = new List<RefundCalculation>();

    public ICollection<CancellationFeeRecord> FeeRecords { get; set; } = new List<CancellationFeeRecord>();
}
