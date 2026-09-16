using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Common;

namespace MealMate.Domain.CollaborativeCampaigns;

public sealed class CampaignPaymentTransaction : BaseEntity
{
    public Guid CheckoutSessionId { get; set; }

    public Guid CampaignId { get; set; }

    public string PaymentProvider { get; set; } = default!;

    public string ProviderTransactionId { get; set; } = default!;

    public string? ProviderPaymentReference { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "KWD";

    public PaymentTransactionStatus Status { get; set; } = PaymentTransactionStatus.Initiated;

    public DateTime InitiatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    public DateTime? FailedAtUtc { get; set; }

    public DateTime? RefundedAtUtc { get; set; }

    public DateTime? ProcessingLeaseExpiresAtUtc { get; set; }
}
