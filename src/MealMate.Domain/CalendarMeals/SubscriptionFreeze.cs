using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.CalendarMeals;

public sealed class SubscriptionFreeze : AuditableEntity
{
    public Guid SubscriptionId { get; set; }

    public DateOnly StartsOn { get; set; }

    public DateOnly EndsOn { get; set; }

    public int DaysCount { get; set; }

    public VerificationStatus Status { get; set; } = VerificationStatus.Submitted;

    public DateTime RequestedAtUtc { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public ICollection<FreezeLedgerEntry> LedgerEntries { get; set; } = new List<FreezeLedgerEntry>();
}
