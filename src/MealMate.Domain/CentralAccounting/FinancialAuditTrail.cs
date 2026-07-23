using MealMate.Domain.Common;

namespace MealMate.Domain.CentralAccounting;

public sealed class FinancialAuditTrail : BaseEntity
{
    public string Action { get; set; } = default!;

    public string EntityType { get; set; } = default!;

    public Guid? EntityId { get; set; }

    public string? BeforeJson { get; set; }

    public string? AfterJson { get; set; }

    public Guid? ActorUserId { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public string? CorrelationId { get; set; }
}
