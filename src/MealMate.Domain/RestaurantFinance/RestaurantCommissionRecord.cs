using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.RestaurantFinance;

/// <summary>
/// Append-only restaurant commission recognition record.
/// </summary>
public sealed class RestaurantCommissionRecord : BaseEntity
{
    public Guid RestaurantId { get; set; }

    public Guid? SubscriptionDayId { get; set; }

    public Money GrossSupply { get; set; } = default!;

    public Percentage CommissionRate { get; set; } = default!;

    public Money CommissionAmount { get; set; } = default!;

    public DateTime RecognizedAtUtc { get; set; }
}
