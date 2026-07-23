using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.CentralAccounting;

public sealed class ChartOfAccount : AuditableEntity
{
    public string Code { get; set; } = default!;

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public AccountType AccountType { get; set; }

    public Guid? ParentId { get; set; }

    public ChartOfAccount? Parent { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<ChartOfAccount> Children { get; set; } = new List<ChartOfAccount>();

    public ICollection<JournalEntryLine> JournalEntryLines { get; set; } = new List<JournalEntryLine>();
}
