namespace MealMate.Domain.Common;

public sealed class OutboxMessage : BaseEntity
{
    public string EventType { get; set; } = default!;

    public string PayloadJson { get; set; } = default!;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ProcessedAtUtc { get; set; }

    public int RetryCount { get; set; }

    public DateTime? LastAttemptedAtUtc { get; set; }

    public string? LastError { get; set; }
}
