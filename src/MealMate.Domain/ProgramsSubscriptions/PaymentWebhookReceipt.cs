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
}
