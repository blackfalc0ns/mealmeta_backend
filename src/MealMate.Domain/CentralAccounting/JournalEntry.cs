using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.CentralAccounting;

public sealed class JournalEntry : BaseEntity
{
    /// <summary>Null for legacy journals that have not been classified to an entity.</summary>
    public Guid? LegalEntityId { get; set; }

    public Guid? AccountingPeriodId { get; set; }

    public AccountingPeriod? AccountingPeriod { get; set; }

    public DateOnly? BusinessDate { get; set; }

    public string? CurrencyCode { get; set; }

    public string? PayloadHash { get; set; }

    public DateTime? SourceOccurredAtUtc { get; set; }

    public string EntryNumber { get; set; } = default!;

    public JournalEntryStatus Status { get; set; } = JournalEntryStatus.Draft;

    public DateTime? PostedAtUtc { get; set; }

    public string Description { get; set; } = default!;

    public string? CorrelationId { get; set; }

    public string? SourceReference { get; set; }

    public ICollection<JournalEntryLine> Lines { get; set; } = new List<JournalEntryLine>();
}
