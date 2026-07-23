using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.Influencers;

/// <summary>
/// Append-only reversal impact when a subscription cancellation affects commission.
/// </summary>
public sealed class InfluencerCancellationImpact : BaseEntity
{
    public Guid CommissionCalculationId { get; set; }

    public InfluencerCommissionCalculation CommissionCalculation { get; set; } = default!;

    public Guid? CancellationId { get; set; }

    public Money ReversedAmount { get; set; } = default!;

    public DateTime RecordedAtUtc { get; set; }
}
