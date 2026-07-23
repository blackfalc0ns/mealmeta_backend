using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CentralAccounting;

public sealed class FinancialSettlement : AuditableEntity
{
    public Guid? RestaurantId { get; set; }

    public int PeriodYear { get; set; }

    public int PeriodMonth { get; set; }

    public PayoutStatus Status { get; set; } = PayoutStatus.Pending;

    public Money GrossTotal { get; set; } = default!;

    public Money NetTotal { get; set; } = default!;

    public DateTime? SettledAtUtc { get; set; }
}
