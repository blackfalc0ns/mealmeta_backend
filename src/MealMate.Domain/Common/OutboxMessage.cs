namespace MealMate.Domain.Common;

public sealed class OutboxMessage : BaseEntity
{
    public string EventType { get; set; } = default!;

    public string? AggregateType { get; set; }

    public string? AggregateId { get; set; }

    public string? CorrelationId { get; set; }

    public string? DeduplicationKey { get; set; }

    public int EventVersion { get; set; } = 1;

    public string PayloadJson { get; set; } = default!;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ProcessedAtUtc { get; set; }

    public int RetryCount { get; set; }

    public DateTime? LastAttemptedAtUtc { get; set; }

    public DateTime? NextAttemptAtUtc { get; set; }

    public DateTime? PoisonedAtUtc { get; set; }

    public string? LastError { get; set; }
}
