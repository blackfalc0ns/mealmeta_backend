namespace MealMate.Domain.Common;

public sealed class InboxProcessedEvent : BaseEntity
{
    public string ConsumerName { get; set; } = default!;

    public Guid EventId { get; set; }

    public DateTime ProcessedAtUtc { get; set; }
}
