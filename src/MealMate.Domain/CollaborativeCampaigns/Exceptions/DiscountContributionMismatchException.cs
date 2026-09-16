using MealMate.Domain.Exceptions;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CollaborativeCampaigns.Exceptions;

public sealed class DiscountContributionMismatchException : DomainException
{
    public Percentage HeadlineDiscount { get; }
    public Percentage RestaurantContribution { get; }
    public Percentage PlatformContribution { get; }

    public DiscountContributionMismatchException(
        Percentage headline,
        Percentage restaurant,
        Percentage platform)
        : base("DISCOUNT_CONTRIBUTION_MISMATCH",
            $"Headline discount ({headline.ToString()}) must equal restaurant contribution ({restaurant.ToString()}) + platform contribution ({platform.ToString()}).")
    {
        HeadlineDiscount = headline;
        RestaurantContribution = restaurant;
        PlatformContribution = platform;
    }
}
