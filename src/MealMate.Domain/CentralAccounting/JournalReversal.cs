using MealMate.Domain.Common;

namespace MealMate.Domain.CentralAccounting;

public sealed class JournalReversal : BaseEntity
{
    public Guid LegalEntityId { get; set; }
    public Guid OriginalJournalEntryId { get; set; }
    public JournalEntry OriginalJournalEntry { get; set; } = default!;
    public Guid ReversalJournalEntryId { get; set; }
    public JournalEntry ReversalJournalEntry { get; set; } = default!;
    public Guid SourceDocumentId { get; set; }
    public string Reason { get; set; } = default!;
    public string EvidenceReference { get; set; } = default!;
    public DateOnly OriginalBusinessDate { get; set; }
    public DateOnly ReversalBusinessDate { get; set; }
}
