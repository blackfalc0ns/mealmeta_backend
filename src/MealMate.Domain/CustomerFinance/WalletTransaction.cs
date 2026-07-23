using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CustomerFinance;

public sealed class WalletTransaction : BaseEntity
{
    public Guid WalletId { get; set; }

    public WalletType WalletType { get; set; }

    public Guid CustomerUserId { get; set; }

    public WalletTransactionType Type { get; set; }

    public Money Amount { get; set; } = default!;

    public Money BalanceAfter { get; set; } = default!;

    public string? ReferenceType { get; set; }

    public Guid? ReferenceId { get; set; }

    public string? CorrelationId { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public string? Description { get; set; }
}
