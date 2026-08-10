using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions.Pricing;

/// <summary>
/// Amendment MM-PRC-AMD-01: campaign discount funding on a single delivered box.
/// Reference price is never used here — only contracted supply and customer sale price.
/// </summary>
public sealed record CampaignBoxEconomicsRequest(
    decimal CustomerPriceBeforeDiscount,
    decimal DiscountRate,
    decimal ContractedSupplyDailyGross,
    decimal CommissionRate,
    CampaignFundingMode FundingMode,
    decimal? RestaurantShareRate = null);

public sealed record CampaignBoxEconomicsResult(
    decimal CustomerDiscount,
    decimal CustomerPriceAfterDiscount,
    decimal MealMateFundingNet,
    decimal RestaurantFundingNet,
    decimal BaseSupplyGross,
    decimal BaseCommission,
    decimal BaseRestaurantNet,
    decimal? CampaignSupplyGross,
    decimal CampaignCommission,
    decimal CampaignRestaurantNet,
    decimal MealMateContributionBeforeOtherCosts);

public static class CampaignBoxEconomicsCalculator
{
    public static CampaignBoxEconomicsResult Calculate(CampaignBoxEconomicsRequest request)
    {
        if (request.CustomerPriceBeforeDiscount < 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Customer price must be >= 0.");
        if (request.DiscountRate is < 0m or > 1m)
            throw new ArgumentOutOfRangeException(nameof(request), "Discount rate must be between 0 and 1.");
        if (request.ContractedSupplyDailyGross < 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Supply gross must be >= 0.");
        if (request.CommissionRate is < 0m or >= 1m)
            throw new ArgumentOutOfRangeException(nameof(request), "Commission rate must be in [0, 1).");

        var customerDiscount = Round3(request.CustomerPriceBeforeDiscount * request.DiscountRate);
        var customerAfter = Round3(request.CustomerPriceBeforeDiscount - customerDiscount);

        var baseSupply = Round3(request.ContractedSupplyDailyGross);
        var baseCommission = Round3(baseSupply * request.CommissionRate);
        var baseRestaurantNet = Round3(baseSupply - baseCommission);
        var baseMealMateContribution = Round3(request.CustomerPriceBeforeDiscount - baseSupply);

        var restaurantShare = ResolveRestaurantShare(request.FundingMode, request.RestaurantShareRate);
        var restaurantFundingNet = Round3(customerDiscount * restaurantShare);
        var mealMateFundingNet = Round3(customerDiscount - restaurantFundingNet);

        // MealMate-funded campaigns leave supply / commission / restaurant net unchanged.
        if (restaurantFundingNet <= 0m)
        {
            return new CampaignBoxEconomicsResult(
                CustomerDiscount: customerDiscount,
                CustomerPriceAfterDiscount: customerAfter,
                MealMateFundingNet: mealMateFundingNet,
                RestaurantFundingNet: 0m,
                BaseSupplyGross: baseSupply,
                BaseCommission: baseCommission,
                BaseRestaurantNet: baseRestaurantNet,
                CampaignSupplyGross: null,
                CampaignCommission: baseCommission,
                CampaignRestaurantNet: baseRestaurantNet,
                MealMateContributionBeforeOtherCosts: Round3(baseMealMateContribution - mealMateFundingNet));
        }

        // Restaurant (or shared) funding reduces restaurant net by the agreed net amount,
        // then rebuilds a temporary campaign supply gross so commission rate stays correct.
        var campaignRestaurantNet = Round3(baseRestaurantNet - restaurantFundingNet);
        if (campaignRestaurantNet < 0m)
            campaignRestaurantNet = 0m;

        var campaignSupplyGross = Round3(campaignRestaurantNet / (1m - request.CommissionRate));
        var campaignCommission = Round3(campaignSupplyGross * request.CommissionRate);

        return new CampaignBoxEconomicsResult(
            CustomerDiscount: customerDiscount,
            CustomerPriceAfterDiscount: customerAfter,
            MealMateFundingNet: mealMateFundingNet,
            RestaurantFundingNet: restaurantFundingNet,
            BaseSupplyGross: baseSupply,
            BaseCommission: baseCommission,
            BaseRestaurantNet: baseRestaurantNet,
            CampaignSupplyGross: campaignSupplyGross,
            CampaignCommission: campaignCommission,
            CampaignRestaurantNet: campaignRestaurantNet,
            MealMateContributionBeforeOtherCosts: Round3(customerAfter - campaignSupplyGross));
    }

    private static decimal ResolveRestaurantShare(CampaignFundingMode mode, decimal? restaurantShareRate)
    {
        return mode switch
        {
            CampaignFundingMode.MealMateFull => 0m,
            CampaignFundingMode.RestaurantFull => 1m,
            CampaignFundingMode.Shared => Math.Clamp(restaurantShareRate ?? 0.5m, 0m, 1m),
            _ => Math.Clamp(restaurantShareRate ?? 0.5m, 0m, 1m),
        };
    }

    private static decimal Round3(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}
