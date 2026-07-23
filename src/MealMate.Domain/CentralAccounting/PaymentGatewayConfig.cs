using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CentralAccounting;

public sealed class PaymentGatewayConfig : AuditableEntity
{
    public string Code { get; set; } = default!;

    public string Name { get; set; } = default!;

    public Percentage? RelativeFee { get; set; }

    public Money? FixedFee { get; set; }

    public Money? MinFee { get; set; }

    public Money? MaxFee { get; set; }

    public int Weight { get; set; }

    public bool IsActive { get; set; } = true;

    public Guid? RuleVersionId { get; set; }
}
