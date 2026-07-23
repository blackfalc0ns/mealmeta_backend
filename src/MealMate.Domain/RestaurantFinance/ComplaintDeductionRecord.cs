using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.RestaurantFinance;

public sealed class ComplaintDeductionRecord : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public Guid ComplaintId { get; set; }

    public Money Amount { get; set; } = default!;

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public DateTime? AppliedAtUtc { get; set; }
}
