using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CentralAccounting;

public sealed class DeferredRevenue : AuditableEntity
{
    public Guid SubscriptionId { get; set; }

    public Money OriginalAmount { get; set; } = default!;

    public Money RemainingAmount { get; set; } = default!;

    public string Status { get; set; } = default!;
}
