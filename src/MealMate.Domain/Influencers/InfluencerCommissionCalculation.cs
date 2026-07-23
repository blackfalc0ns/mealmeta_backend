using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.Influencers;

/// <summary>
/// Append-only influencer commission calculation.
/// </summary>
public sealed class InfluencerCommissionCalculation : BaseEntity
{
    public Guid InfluencerProfileId { get; set; }

    public Guid SubscriptionId { get; set; }

    public Guid? CodeId { get; set; }

    public InfluencerCode? Code { get; set; }

    public Money PaidAmountAfterDiscount { get; set; } = default!;

    public Percentage CommissionRate { get; set; } = default!;

    public Money CommissionAmount { get; set; } = default!;

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public DateTime CalculatedAtUtc { get; set; }

    public ICollection<InfluencerCancellationImpact> CancellationImpacts { get; set; } = new List<InfluencerCancellationImpact>();
}
