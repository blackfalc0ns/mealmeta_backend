using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CollaborativeCampaigns.ValueObjects;

public sealed class CampaignPriceCalculationResult : ValueObject
{
    public Money ReferenceCustomerPrice { get; }
    public Percentage HeadlineDiscount { get; }
    public Percentage RestaurantContribution { get; }
    public Percentage PlatformContribution { get; }
    public Money CustomerDiscountAmount { get; }
    public Money CampaignCustomerPrice { get; }
    public Money RestaurantContributionFunded { get; }
    public Money PlatformContributionFunded { get; }
    public int DurationDays { get; }

    public CampaignPriceCalculationResult(
        Money referenceCustomerPrice,
        Percentage headlineDiscount,
        Percentage restaurantContribution,
        Percentage platformContribution,
        Money customerDiscountAmount,
        Money campaignCustomerPrice,
        Money restaurantContributionFunded,
        Money platformContributionFunded,
        int durationDays)
    {
        ReferenceCustomerPrice = referenceCustomerPrice;
        HeadlineDiscount = headlineDiscount;
        RestaurantContribution = restaurantContribution;
        PlatformContribution = platformContribution;
        CustomerDiscountAmount = customerDiscountAmount;
        CampaignCustomerPrice = campaignCustomerPrice;
        RestaurantContributionFunded = restaurantContributionFunded;
        PlatformContributionFunded = platformContributionFunded;
        DurationDays = durationDays;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ReferenceCustomerPrice;
        yield return HeadlineDiscount.Value;
        yield return RestaurantContribution.Value;
        yield return PlatformContribution.Value;
        yield return CustomerDiscountAmount;
        yield return CampaignCustomerPrice;
        yield return RestaurantContributionFunded;
        yield return PlatformContributionFunded;
        yield return DurationDays;
    }
}
