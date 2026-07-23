using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.RestaurantFinance;

/// <summary>
/// Append-only restaurant payable recognition for a subscription day.
/// </summary>
public sealed class MealPayable : BaseEntity
{
    public Guid RestaurantId { get; set; }

    public Guid SubscriptionDayId { get; set; }

    public Guid? OrderId { get; set; }

    public Money GrossSupply { get; set; } = default!;

    public Money Commission { get; set; } = default!;

    public Money? CampaignContributionNet { get; set; }

    public Money NetPayable { get; set; } = default!;

    public DateTime RecognizedAtUtc { get; set; }

    public string? SnapshotJson { get; set; }
}
