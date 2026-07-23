using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CustomerFinance;

public sealed class CancellationFeeRecord : AuditableEntity
{
    public Guid CancellationId { get; set; }

    public SubscriptionCancellation Cancellation { get; set; } = default!;

    public Percentage FeeRate { get; set; } = default!;

    public Money FeeAmount { get; set; } = default!;

    public Guid? RuleVersionId { get; set; }
}
