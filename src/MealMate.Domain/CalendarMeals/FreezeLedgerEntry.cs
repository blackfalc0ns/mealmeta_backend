using MealMate.Domain.Common;

namespace MealMate.Domain.CalendarMeals;

/// <summary>
/// Append-only freeze ledger entry. Must not be soft-deleted (audit trail).
/// </summary>
public sealed class FreezeLedgerEntry : BaseEntity
{
    public Guid SubscriptionId { get; set; }

    public Guid FreezeId { get; set; }

    public SubscriptionFreeze Freeze { get; set; } = default!;

    public int DaysAdded { get; set; }

    public DateOnly EndDateBefore { get; set; }

    public DateOnly EndDateAfter { get; set; }

    public DateTime RecordedAtUtc { get; set; }
}
