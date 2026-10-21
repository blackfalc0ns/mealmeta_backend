namespace MealMate.Domain.Common;

public sealed class InboxProcessedEvent : BaseEntity
{
    public string ConsumerName { get; set; } = default!;

    public Guid EventId { get; set; }

    public DateTime ProcessedAtUtc { get; set; }

    /// <summary>Set only by the finance consumer to detect economic-content conflicts.</summary>
    public string? FinancePayloadHash { get; set; }

    /// <summary>Persisted outcome for finance event replay; null for all existing consumers.</summary>
    public Guid? ResultJournalEntryId { get; set; }
}
