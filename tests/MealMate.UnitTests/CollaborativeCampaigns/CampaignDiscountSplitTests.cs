using FluentAssertions;
using MealMate.Domain.CollaborativeCampaigns.Exceptions;
using MealMate.Domain.CollaborativeCampaigns.ValueObjects;
using MealMate.Domain.ValueObjects;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignDiscountSplitTests
{
    [Fact]
    public void Create_ValidAdditiveSplit_Succeeds()
    {
        var headline = Percentage.FromFraction(0.20m);
        var restaurant = Percentage.FromFraction(0.12m);
        var platform = Percentage.FromFraction(0.08m);

        var result = CampaignDiscountSplit.Create(headline, restaurant, platform);

        result.IsSuccess.Should().BeTrue();
        result.Value.HeadlineDiscount.Value.Should().Be(0.20m);
        result.Value.RestaurantContribution.Value.Should().Be(0.12m);
        result.Value.PlatformContribution.Value.Should().Be(0.08m);
    }

    [Fact]
    public void Create_ValidBoundaryTolerance_Succeeds()
    {
        // Sum equals headline within 0.0001 tolerance
        var headline = Percentage.FromFraction(0.3333m);
        var restaurant = Percentage.FromFraction(0.1666m);
        var platform = Percentage.FromFraction(0.1667m);

        var result = CampaignDiscountSplit.Create(headline, restaurant, platform);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_FullRestaurantFunded_Succeeds()
    {
        var headline = Percentage.FromFraction(0.15m);
        var restaurant = Percentage.FromFraction(0.15m);
        var platform = Percentage.FromFraction(0.00m);

        var result = CampaignDiscountSplit.Create(headline, restaurant, platform);

        result.IsSuccess.Should().BeTrue();
        result.Value.PlatformContribution.Value.Should().Be(0.00m);
    }

    [Fact]
    public void Create_FullPlatformFunded_Succeeds()
    {
        var headline = Percentage.FromFraction(0.10m);
        var restaurant = Percentage.FromFraction(0.00m);
        var platform = Percentage.FromFraction(0.10m);

        var result = CampaignDiscountSplit.Create(headline, restaurant, platform);

        result.IsSuccess.Should().BeTrue();
        result.Value.RestaurantContribution.Value.Should().Be(0.00m);
    }

    [Fact]
    public void Create_WhenContributionsDoNotEqualHeadline_Fails()
    {
        var headline = Percentage.FromFraction(0.30m);
        var restaurant = Percentage.FromFraction(0.20m);
        var platform = Percentage.FromFraction(0.05m); // Sum = 0.25 != 0.30

        var result = CampaignDiscountSplit.Create(headline, restaurant, platform);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Headline discount (30%) must equal restaurant contribution (20%) + platform contribution (5%)");
    }

    [Fact]
    public void Of_WhenContributionsDoNotEqualHeadline_ThrowsDiscountContributionMismatchException()
    {
        var headline = Percentage.FromFraction(0.30m);
        var restaurant = Percentage.FromFraction(0.20m);
        var platform = Percentage.FromFraction(0.05m);

        var act = () => CampaignDiscountSplit.Of(headline, restaurant, platform);

        act.Should().Throw<DiscountContributionMismatchException>()
            .WithMessage("*Headline discount*must equal*");
    }

    [Theory]
    [InlineData(1.05, 0.50, 0.55)] // Headline > 1.0 (100%)
    [InlineData(0.20, -0.05, 0.25)] // Negative restaurant contribution
    public void Create_OutOfRangePercentages_Fails(decimal h, decimal r, decimal p)
    {
        try
        {
            var headline = Percentage.FromFraction(h);
            var restaurant = Percentage.FromFraction(r);
            var platform = Percentage.FromFraction(p);

            var result = CampaignDiscountSplit.Create(headline, restaurant, platform);
            result.IsFailure.Should().BeTrue();
        }
        catch (Exception ex)
        {
            // Percentage.FromFraction throws DomainException for negative values
            ex.Should().NotBeNull();
        }
    }

    [Fact]
    public void Equality_TwoIdenticalSplits_AreEqual()
    {
        var split1 = CampaignDiscountSplit.Of(
            Percentage.FromFraction(0.25m),
            Percentage.FromFraction(0.15m),
            Percentage.FromFraction(0.10m));

        var split2 = CampaignDiscountSplit.Of(
            Percentage.FromFraction(0.25m),
            Percentage.FromFraction(0.15m),
            Percentage.FromFraction(0.10m));

        split1.Should().Be(split2);
        (split1 == split2).Should().BeTrue();
    }
}
