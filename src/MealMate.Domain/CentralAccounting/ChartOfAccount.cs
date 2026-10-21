using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.CentralAccounting;

public sealed class ChartOfAccount : AuditableEntity
{
    /// <summary>Null only for legacy/unclassified historical accounts.</summary>
    public Guid? LegalEntityId { get; set; }

    public string Code { get; set; } = default!;

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public AccountType AccountType { get; set; }

    public AccountNormalBalance? NormalBalance { get; set; }

    public Guid? ParentId { get; set; }

    public ChartOfAccount? Parent { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsPostable { get; set; }

    public bool RequiresParty { get; set; }

    public DateOnly? EffectiveFrom { get; set; }

    public DateOnly? EffectiveToExclusive { get; set; }

    public string? ReportingLineCode { get; set; }

    public ICollection<ChartOfAccount> Children { get; set; } = new List<ChartOfAccount>();

    public ICollection<JournalEntryLine> JournalEntryLines { get; set; } = new List<JournalEntryLine>();
}
