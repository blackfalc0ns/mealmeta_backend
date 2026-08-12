using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.RestaurantOperations;

/// <summary>Temporary or confirmed reservation of one restaurant capacity unit for a subscription service day.</summary>
public sealed class CapacityHold : AuditableEntity
{
    public Guid RestaurantId { get; set; }
    public Guid SubscriptionId { get; set; }

    /// <summary>Approved menu version resolved at checkout; immutable for this reservation.</summary>
    public Guid MenuPlanVersionId { get; set; }
    public DateOnly ServiceDate { get; set; }
    public int Quantity { get; set; } = 1;
    public CapacityHoldStatus Status { get; set; } = CapacityHoldStatus.PendingPayment;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }
}
