using MealMate.Domain.Common;

namespace MealMate.Domain.IdentityAccess;

/// <summary>Append-only system audit trail. Never soft-deleted.</summary>
public sealed class AuditLog : BaseEntity
{
    public Guid? ActorUserId { get; set; }

    public string Action { get; set; } = default!;

    public string EntityType { get; set; } = default!;

    public Guid? EntityId { get; set; }

    public string? BeforeJson { get; set; }

    public string? AfterJson { get; set; }

    public string? Reason { get; set; }

    public string? CorrelationId { get; set; }

    public string? SourceChannel { get; set; }

    public DateTime OccurredAtUtc { get; set; }
}
