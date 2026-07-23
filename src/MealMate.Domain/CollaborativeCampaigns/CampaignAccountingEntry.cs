using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CollaborativeCampaigns;

/// <summary>
/// Append-only campaign accounting recognition for a delivery day / restaurant.
/// </summary>
public sealed class CampaignAccountingEntry : BaseEntity
{
    public Guid CampaignId { get; set; }

    public Campaign Campaign { get; set; } = default!;

    public Guid? SubscriptionDayId { get; set; }

    public Guid RestaurantId { get; set; }

    public Money CustomerPriceBefore { get; set; } = default!;

    public Money CustomerDiscount { get; set; } = default!;

    public Money CustomerPriceAfter { get; set; } = default!;

    public CampaignFundingMode FundingMode { get; set; }

    public Money MealMateFundingNet { get; set; } = default!;

    public Money RestaurantFundingNet { get; set; } = default!;

    public Money BaseSupplyGross { get; set; } = default!;

    public Money? CampaignSupplyGross { get; set; }

    public Money Commission { get; set; } = default!;

    public Money RestaurantNet { get; set; } = default!;

    public Money ReferenceDailyBox { get; set; } = default!;

    public DateTime RecognizedAtUtc { get; set; }
}
