using MealMate.Domain.Common;

namespace MealMate.Domain.CentralAccounting;

/// <summary>One immutable economic effect, independent of transport EventId or handler version.</summary>
public sealed class PostingIdentity : BaseEntity
{
    public Guid LegalEntityId { get; set; }
    public string SourceType { get; set; } = default!;
    public Guid SourceId { get; set; }
    public Guid ServiceDayKey { get; set; }
    public PostingEffect Effect { get; set; }
    public Guid AdjustmentKey { get; set; }
    public string EconomicHash { get; set; } = default!;
    public Guid JournalEntryId { get; set; }
    public JournalEntry JournalEntry { get; set; } = default!;
}
