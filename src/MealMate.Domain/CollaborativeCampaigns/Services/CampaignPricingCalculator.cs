using MealMate.Domain.CollaborativeCampaigns.ValueObjects;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CollaborativeCampaigns.Services;

public sealed class CampaignPricingCalculator
{
    private const int Precision = 3;
    private const MidpointRounding Rounding = MidpointRounding.AwayFromZero;

    public CampaignPriceCalculationResult Calculate(
        Money referenceCustomerPrice,
        ValueObjects.CampaignDiscountSplit discountSplit,
        int durationDays)
    {
        if (referenceCustomerPrice is null || referenceCustomerPrice.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(referenceCustomerPrice), "Reference customer price must be greater than zero.");
        }

        if (durationDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationDays), "Duration days must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(discountSplit);

        var currency = referenceCustomerPrice.CurrencyCode;
        var refAmount = referenceCustomerPrice.Amount;

        var totalDiscountAmount = Math.Round(refAmount * discountSplit.HeadlineDiscount.Value, Precision, Rounding);
        var campaignCustomerPriceAmount = refAmount - totalDiscountAmount;

        var restaurantContributionAmount = Math.Round(refAmount * discountSplit.RestaurantContribution.Value, Precision, Rounding);
        var platformContributionAmount = totalDiscountAmount - restaurantContributionAmount;

        return new CampaignPriceCalculationResult(
            referenceCustomerPrice,
            discountSplit.HeadlineDiscount,
            discountSplit.RestaurantContribution,
            discountSplit.PlatformContribution,
            new Money(totalDiscountAmount, currency),
            new Money(campaignCustomerPriceAmount, currency),
            new Money(restaurantContributionAmount, currency),
            new Money(platformContributionAmount, currency),
            durationDays);
    }
}
