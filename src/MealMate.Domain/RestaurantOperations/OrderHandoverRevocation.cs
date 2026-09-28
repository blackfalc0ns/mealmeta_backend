using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class OrderHandoverRevocation : AuditableEntity
{
    public Guid HandoverId { get; set; }
    public OrderHandover Handover { get; set; } = default!;
    public DateTime RevokedAtUtc { get; set; }
    public Guid RevokedByUserId { get; set; }
    public string Reason { get; set; } = default!;
}
