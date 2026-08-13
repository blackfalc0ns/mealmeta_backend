using MealMate.Domain.Common;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class PaymentWebhookReceipt : AuditableEntity
{
    public string Gateway { get; set; } = default!;
    public string EventKey { get; set; } = default!;
    public Guid? SubscriptionId { get; set; }
    public string Status { get; set; } = default!;
    public string? PayloadHash { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
    public string? PaymentId { get; set; }
    public string? InvoiceId { get; set; }
    public decimal? Amount { get; set; }
    public string? CurrencyCode { get; set; }
    public string? CustomerReference { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public int FulfillmentAttemptCount { get; set; }
    public string? LastFulfillmentErrorCode { get; set; }
    public DateTime? LastFulfillmentAttemptUtc { get; set; }
    public DateTime? FulfillmentCompletedAtUtc { get; set; }
}
