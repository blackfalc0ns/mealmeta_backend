using MealMate.Domain.Common;

namespace MealMate.Domain.ComplaintsSupport;

public sealed class SupportCaseEvent : BaseEntity
{
    public Guid SupportCaseId { get; set; }
    public Guid ActorUserId { get; set; }
    public string EventType { get; set; } = default!;
    public string? FromValue { get; set; }
    public string? ToValue { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? PayloadHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
