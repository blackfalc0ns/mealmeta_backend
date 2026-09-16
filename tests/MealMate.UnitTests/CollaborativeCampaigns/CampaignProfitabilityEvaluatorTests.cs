using FluentAssertions;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignProfitabilityEvaluatorTests
{
    private readonly CampaignProfitabilityEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_HealthyMarginAboveThreshold_MarksViableWithoutOverride()
    {
        var campaignId = Guid.NewGuid();
        // Customer pays 100 KWD
        // Restaurant base price is 80 KWD, restaurant contribution 10% (concession 8 KWD), commission 15% (12 KWD)
        // Restaurant net payout = 80 - 8 - 12 = 60 KWD
        // Platform profit = 100 - 60 = 40 KWD -> Margin = 40 / 100 = 40%
        var pricingInput = new List<DurationPricingInput>
        {
            new(
                DurationDays: 26,
                ReferenceCustomerPrice: 125m,
                CampaignCustomerPrice: 100m,
                RestaurantOffers: new List<RestaurantOfferInput>
                {
                    new(RestaurantId: Guid.NewGuid(), RestaurantBasePrice: 80m, RestaurantContributionPct: 0.10m, CommissionRate: 0.15m, Weight: 1.0m)
                }
            )
        };

        var snapshot = _evaluator.Evaluate(campaignId, pricingInput, profitabilityVersion: 1);

        snapshot.Should().NotBeNull();
        snapshot.OverallExpectedMarginPct.Should().BeApproximately(0.40m, 0.001m);
        snapshot.IsViable.Should().BeTrue();
        snapshot.RequiresOverride.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_MarginBetween0And10Percent_MarksRequiresOverride()
    {
        var campaignId = Guid.NewGuid();
        // Customer pays 100 KWD
        // Restaurant base price 100 KWD, contribution 5% (5 KWD), commission 3% (3 KWD) -> net payout = 92 KWD
        // Platform profit = 100 - 92 = 8 KWD -> Margin = 8 / 100 = 8% (below 10% default threshold)
        var pricingInput = new List<DurationPricingInput>
        {
            new(
                DurationDays: 26,
                ReferenceCustomerPrice: 120m,
                CampaignCustomerPrice: 100m,
                RestaurantOffers: new List<RestaurantOfferInput>
                {
                    new(RestaurantId: Guid.NewGuid(), RestaurantBasePrice: 100m, RestaurantContributionPct: 0.05m, CommissionRate: 0.03m, Weight: 1.0m)
                }
            )
        };

        var snapshot = _evaluator.Evaluate(campaignId, pricingInput);

        snapshot.Should().NotBeNull();
        snapshot.OverallExpectedMarginPct.Should().BeApproximately(0.08m, 0.001m);
        snapshot.IsViable.Should().BeTrue();
        snapshot.RequiresOverride.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_NegativeMargin_MarksNotViableAndRequiresOverride()
    {
        var campaignId = Guid.NewGuid();
        // Customer pays 70 KWD
        // Restaurant net payout is 85 KWD
        // Platform profit = 70 - 85 = -15 KWD -> Margin = -21.4%
        var pricingInput = new List<DurationPricingInput>
        {
            new(
                DurationDays: 26,
                ReferenceCustomerPrice: 100m,
                CampaignCustomerPrice: 70m,
                RestaurantOffers: new List<RestaurantOfferInput>
                {
                    new(RestaurantId: Guid.NewGuid(), RestaurantBasePrice: 90m, RestaurantContributionPct: 0.00m, CommissionRate: 0.05m, Weight: 1.0m)
                }
            )
        };

        var snapshot = _evaluator.Evaluate(campaignId, pricingInput);

        snapshot.Should().NotBeNull();
        snapshot.OverallExpectedMarginPct.Should().BeLessThan(0);
        snapshot.IsViable.Should().BeFalse();
        snapshot.RequiresOverride.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_MultipleRestaurants_ComputesWorstCaseMarginCorrectly()
    {
        var campaignId = Guid.NewGuid();
        var pricingInput = new List<DurationPricingInput>
        {
            new(
                DurationDays: 26,
                ReferenceCustomerPrice: 100m,
                CampaignCustomerPrice: 90m,
                RestaurantOffers: new List<RestaurantOfferInput>
                {
                    // R1: Net payout = 70 -> Profit = 20 -> Margin = 22.2%
                    new(RestaurantId: Guid.NewGuid(), RestaurantBasePrice: 80m, RestaurantContributionPct: 0.05m, CommissionRate: 0.10m, Weight: 0.5m),
                    // R2: Net payout = 85 -> Profit = 5 -> Margin = 5.5% (Worst case)
                    new(RestaurantId: Guid.NewGuid(), RestaurantBasePrice: 90m, RestaurantContributionPct: 0.00m, CommissionRate: 0.05m, Weight: 0.5m)
                }
            )
        };

        var snapshot = _evaluator.Evaluate(campaignId, pricingInput);

        snapshot.Should().NotBeNull();
        snapshot.OverallWorstCaseMarginPct.Should().BeApproximately(0.05m, 0.001m);
    }
}
