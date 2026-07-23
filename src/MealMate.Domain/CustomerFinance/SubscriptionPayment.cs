using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CustomerFinance;

public sealed class SubscriptionPayment : AuditableEntity
{
    public Guid SubscriptionId { get; set; }

    public Guid CustomerUserId { get; set; }

    public Money Amount { get; set; } = default!;

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public string? GatewayCode { get; set; }

    public string? GatewayTransactionId { get; set; }

    public DateTime? PaidAtUtc { get; set; }
}
