using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CentralAccounting;

public sealed class JournalEntryLine : BaseEntity
{
    public Guid JournalEntryId { get; set; }

    public JournalEntry JournalEntry { get; set; } = default!;

    public Guid AccountId { get; set; }

    public ChartOfAccount Account { get; set; } = default!;

    public Money? Debit { get; set; }

    public Money? Credit { get; set; }

    public string? Memo { get; set; }
}
