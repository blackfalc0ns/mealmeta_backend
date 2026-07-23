using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CentralAccounting;

public sealed class DailyRevenueRecognition : BaseEntity
{
    public Guid SubscriptionDayId { get; set; }

    public Guid SubscriptionId { get; set; }

    public Money Amount { get; set; } = default!;

    public DateTime RecognizedAtUtc { get; set; }

    public Guid? JournalEntryId { get; set; }

    public JournalEntry? JournalEntry { get; set; }
}
