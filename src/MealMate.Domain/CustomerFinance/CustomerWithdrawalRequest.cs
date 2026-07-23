using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CustomerFinance;

public sealed class CustomerWithdrawalRequest : AuditableEntity
{
    public Guid CustomerUserId { get; set; }

    public Money Amount { get; set; } = default!;

    public WithdrawalStatus Status { get; set; } = WithdrawalStatus.Requested;

    public DateTime RequestedAtUtc { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public Guid? ReviewedByUserId { get; set; }
}
