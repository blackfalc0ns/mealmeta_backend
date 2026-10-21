using MealMate.Domain.Common;

namespace MealMate.Domain.CentralAccounting;

/// <summary>Official trace link from one approved source document to its posted journal.</summary>
public sealed class JournalSourceLink : BaseEntity
{
    public Guid LegalEntityId { get; set; }
    public Guid SourceDocumentId { get; set; }
    public JournalSourceDocument SourceDocument { get; set; } = default!;
    public Guid JournalEntryId { get; set; }
    public JournalEntry JournalEntry { get; set; } = default!;
    public JournalSourceLinkType Type { get; set; }
    public string SourceFingerprint { get; set; } = default!;
    public DateTime LinkedAtUtc { get; set; }
}
