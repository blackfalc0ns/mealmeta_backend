using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CustomerFinance;

public sealed class CustomerCashWallet : AuditableEntity
{
    public Guid CustomerUserId { get; set; }

    public Money Balance { get; set; } = default!;

    public bool IsActive { get; set; } = true;
}
