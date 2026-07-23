using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.RestaurantFinance;

public sealed class RestaurantAccountStatement : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public int PeriodYear { get; set; }

    public int PeriodMonth { get; set; }

    public Money OpeningBalance { get; set; } = default!;

    public Money ClosingBalance { get; set; } = default!;

    public DateTime GeneratedAtUtc { get; set; }

    public string? SnapshotJson { get; set; }
}
