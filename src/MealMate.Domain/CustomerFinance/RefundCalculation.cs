using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CustomerFinance;

public sealed class RefundCalculation : BaseEntity
{
    public Guid CancellationId { get; set; }

    public SubscriptionCancellation Cancellation { get; set; } = default!;

    public Money RefundEligiblePaid { get; set; } = default!;

    public Money UnusedCashValue { get; set; } = default!;

    public Money CancellationFee { get; set; } = default!;

    public Money FinalCashRefund { get; set; } = default!;

    public DateTime CalculatedAtUtc { get; set; }

    public string? SnapshotJson { get; set; }
}
