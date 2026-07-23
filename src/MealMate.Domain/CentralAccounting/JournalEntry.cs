using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.CentralAccounting;

public sealed class JournalEntry : BaseEntity
{
    public string EntryNumber { get; set; } = default!;

    public JournalEntryStatus Status { get; set; } = JournalEntryStatus.Draft;

    public DateTime? PostedAtUtc { get; set; }

    public string Description { get; set; } = default!;

    public string? CorrelationId { get; set; }

    public string? SourceReference { get; set; }

    public ICollection<JournalEntryLine> Lines { get; set; } = new List<JournalEntryLine>();
}
