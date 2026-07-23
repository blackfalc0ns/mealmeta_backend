using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CentralAccounting;

public sealed class InfluencerPayableLedger : BaseEntity
{
    public Guid InfluencerProfileId { get; set; }

    public Guid SubscriptionId { get; set; }

    public Money CommissionAmount { get; set; } = default!;

    public string Status { get; set; } = default!;

    public DateTime RecognizedAtUtc { get; set; }
}
