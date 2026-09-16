using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CollaborativeCampaigns.ValueObjects;

public sealed class RestaurantSettlementResult : ValueObject
{
    public Money RestaurantBasePrice { get; }
    public Money ReferenceCustomerPrice { get; }
    public Percentage RestaurantContributionPct { get; }
    public Percentage CommissionRate { get; }
    public Money RestaurantDiscountConcession { get; }
    public Money GrossRestaurantSettlement { get; }
    public Money CommissionAmount { get; }
    public Money RestaurantNetPayout { get; }
    public Money WholesalePricingSpread { get; }

    public RestaurantSettlementResult(
        Money restaurantBasePrice,
        Money referenceCustomerPrice,
        Percentage restaurantContributionPct,
        Percentage commissionRate,
        Money restaurantDiscountConcession,
        Money grossRestaurantSettlement,
        Money commissionAmount,
        Money restaurantNetPayout,
        Money wholesalePricingSpread)
    {
        RestaurantBasePrice = restaurantBasePrice;
        ReferenceCustomerPrice = referenceCustomerPrice;
        RestaurantContributionPct = restaurantContributionPct;
        CommissionRate = commissionRate;
        RestaurantDiscountConcession = restaurantDiscountConcession;
        GrossRestaurantSettlement = grossRestaurantSettlement;
        CommissionAmount = commissionAmount;
        RestaurantNetPayout = restaurantNetPayout;
        WholesalePricingSpread = wholesalePricingSpread;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return RestaurantBasePrice;
        yield return ReferenceCustomerPrice;
        yield return RestaurantContributionPct.Value;
        yield return CommissionRate.Value;
        yield return RestaurantDiscountConcession;
        yield return GrossRestaurantSettlement;
        yield return CommissionAmount;
        yield return RestaurantNetPayout;
        yield return WholesalePricingSpread;
    }
}
