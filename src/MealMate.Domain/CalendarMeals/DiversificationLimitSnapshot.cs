using MealMate.Domain.Common;

namespace MealMate.Domain.CalendarMeals;

/// <summary>
/// Append-only diversification limit snapshot. Must not be soft-deleted (audit trail).
/// </summary>
public sealed class DiversificationLimitSnapshot : BaseEntity
{
    public Guid SubscriptionId { get; set; }

    public int NSnapshot { get; set; }

    public int DurationDays { get; set; }

    public decimal RawLimit { get; set; }

    public int LMin { get; set; }

    public int LMax { get; set; }

    public int LimitValue { get; set; }

    public DateTime CapturedAtUtc { get; set; }
}