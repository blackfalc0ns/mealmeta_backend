using MealMate.Domain.Common;

namespace MealMate.Domain.IdentityAccess;

/// <summary>
/// Idempotency log for customer push notifications (push-only; no in-app inbox yet).
/// </summary>
public sealed class CustomerPushDeliveryLog : BaseEntity
{
    public Guid UserId { get; set; }
    public string EventKey { get; set; } = default!;
    public Guid SubjectId { get; set; }
    public DateTime SentAtUtc { get; set; }
}
