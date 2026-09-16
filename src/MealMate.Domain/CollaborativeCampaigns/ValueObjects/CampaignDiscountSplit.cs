using MealMate.Domain.CollaborativeCampaigns.Exceptions;
using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CollaborativeCampaigns.ValueObjects;

public sealed class CampaignDiscountSplit : ValueObject
{
    public Percentage HeadlineDiscount { get; }
    public Percentage RestaurantContribution { get; }
    public Percentage PlatformContribution { get; }

    private CampaignDiscountSplit(
        Percentage headlineDiscount,
        Percentage restaurantContribution,
        Percentage platformContribution)
    {
        HeadlineDiscount = headlineDiscount;
        RestaurantContribution = restaurantContribution;
        PlatformContribution = platformContribution;
    }

    public static Result<CampaignDiscountSplit> Create(
        Percentage headline,
        Percentage restaurant,
        Percentage platform)
    {
        if (headline.Value > 1.0m)
        {
            return Result.Failure<CampaignDiscountSplit>("Headline discount cannot exceed 100%.");
        }

        if (restaurant.Value > headline.Value)
        {
            return Result.Failure<CampaignDiscountSplit>("Restaurant contribution cannot exceed headline discount.");
        }

        if (platform.Value > headline.Value)
        {
            return Result.Failure<CampaignDiscountSplit>("Platform contribution cannot exceed headline discount.");
        }

        if (Math.Abs(headline.Value - (restaurant.Value + platform.Value)) > 0.0001m)
        {
            return Result.Failure<CampaignDiscountSplit>(
                $"Headline discount ({headline.ToString()}) must equal restaurant contribution ({restaurant.ToString()}) + platform contribution ({platform.ToString()}).");
        }

        return Result.Success(new CampaignDiscountSplit(headline, restaurant, platform));
    }

    public static CampaignDiscountSplit Of(
        Percentage headline,
        Percentage restaurant,
        Percentage platform)
    {
        var result = Create(headline, restaurant, platform);
        if (result.IsFailure)
        {
            throw new DiscountContributionMismatchException(headline, restaurant, platform);
        }

        return result.Value;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return HeadlineDiscount.Value;
        yield return RestaurantContribution.Value;
        yield return PlatformContribution.Value;
    }
}
