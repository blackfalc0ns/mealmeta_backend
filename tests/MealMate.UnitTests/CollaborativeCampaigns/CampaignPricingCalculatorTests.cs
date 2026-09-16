using FluentAssertions;
using MealMate.Domain.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns.ValueObjects;
using MealMate.Domain.ValueObjects;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignPricingCalculatorTests
{
    private readonly CampaignPricingCalculator _calculator = new();

    [Fact]
    public void Calculate_StandardScenario_CalculatesAccurately()
    {
        // Reference Customer Price: 60.000 KWD, Duration: 26 days
        // Headline: 20% (12.000 KWD), Restaurant: 12% (7.200 KWD), Platform: 8% (4.800 KWD)
        var referencePrice = new Money(60.000m, "KWD");
        var split = CampaignDiscountSplit.Of(
            Percentage.FromFraction(0.20m),
            Percentage.FromFraction(0.12m),
            Percentage.FromFraction(0.08m));

        var result = _calculator.Calculate(referencePrice, split, 26);

        result.ReferenceCustomerPrice.Amount.Should().Be(60.000m);
        result.CustomerDiscountAmount.Amount.Should().Be(12.000m);
        result.CampaignCustomerPrice.Amount.Should().Be(48.000m);
        result.RestaurantContributionFunded.Amount.Should().Be(7.200m);
        result.PlatformContributionFunded.Amount.Should().Be(4.800m);
        result.DurationDays.Should().Be(26);

        // Invariant: CustomerDiscount = RestaurantContributionFunded + PlatformContributionFunded
        (result.RestaurantContributionFunded.Amount + result.PlatformContributionFunded.Amount)
            .Should().Be(result.CustomerDiscountAmount.Amount);
    }

    [Fact]
    public void Calculate_WithOddAmounts_MaintainsAdditiveSumWithRounding()
    {
        // Reference: 45.750 KWD, Headline: 15% (6.8625 -> 6.863), Restaurant: 10% (4.575), Platform: 5% (2.2875)
        var referencePrice = new Money(45.750m, "KWD");
        var split = CampaignDiscountSplit.Of(
            Percentage.FromFraction(0.15m),
            Percentage.FromFraction(0.10m),
            Percentage.FromFraction(0.05m));

        var result = _calculator.Calculate(referencePrice, split, 14);

        result.ReferenceCustomerPrice.Amount.Should().Be(45.750m);
        // Math.Round(45.750 * 0.15, 3, AwayFromZero) = 6.863
        result.CustomerDiscountAmount.Amount.Should().Be(6.863m);
        // CampaignCustomerPrice = 45.750 - 6.863 = 38.887m
        result.CampaignCustomerPrice.Amount.Should().Be(38.887m);

        // Sum invariant must strictly hold
        (result.RestaurantContributionFunded.Amount + result.PlatformContributionFunded.Amount)
            .Should().Be(result.CustomerDiscountAmount.Amount);
    }

    [Fact]
    public void Calculate_Throws_WhenPriceIsZeroOrNegative()
    {
        var zeroPrice = new Money(0m, "KWD");
        var split = CampaignDiscountSplit.Of(
            Percentage.FromFraction(0.10m),
            Percentage.FromFraction(0.05m),
            Percentage.FromFraction(0.05m));

        var act = () => _calculator.Calculate(zeroPrice, split, 26);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Calculate_Throws_WhenDurationDaysIsZeroOrNegative()
    {
        var price = new Money(50.000m, "KWD");
        var split = CampaignDiscountSplit.Of(
            Percentage.FromFraction(0.10m),
            Percentage.FromFraction(0.05m),
            Percentage.FromFraction(0.05m));

        var act = () => _calculator.Calculate(price, split, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
