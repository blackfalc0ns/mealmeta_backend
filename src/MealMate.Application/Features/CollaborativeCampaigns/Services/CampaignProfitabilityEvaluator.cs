using System.Text.Json;
using MealMate.Domain.CollaborativeCampaigns;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public sealed record RestaurantOfferInput(
    Guid RestaurantId,
    decimal RestaurantBasePrice,
    decimal RestaurantContributionPct,
    decimal CommissionRate,
    decimal Weight);

public sealed record DurationPricingInput(
    int DurationDays,
    decimal ReferenceCustomerPrice,
    decimal CampaignCustomerPrice,
    IReadOnlyList<RestaurantOfferInput> RestaurantOffers);

public interface ICampaignProfitabilityEvaluator
{
    CampaignProfitabilitySnapshot Evaluate(
        Guid campaignId,
        IReadOnlyList<DurationPricingInput> durationPricings,
        int profitabilityVersion = 1,
        decimal minimumExpectedMarginThreshold = 0.10m);
}

public sealed class CampaignProfitabilityEvaluator : ICampaignProfitabilityEvaluator
{
    public CampaignProfitabilitySnapshot Evaluate(
        Guid campaignId,
        IReadOnlyList<DurationPricingInput> durationPricings,
        int profitabilityVersion = 1,
        decimal minimumExpectedMarginThreshold = 0.10m)
    {
        var durationDetails = new List<DurationMarginDetail>();
        decimal totalWeightedProfit = 0m;
        decimal totalWeightedCustomerPrice = 0m;
        decimal globalWorstCaseMargin = decimal.MaxValue;

        foreach (var pricing in durationPricings)
        {
            var customerPrice = pricing.CampaignCustomerPrice;
            if (customerPrice <= 0 || pricing.RestaurantOffers.Count == 0)
            {
                continue;
            }

            decimal durationExpectedPayout = 0m;
            decimal durationWorstCasePayout = 0m;
            decimal totalWeight = pricing.RestaurantOffers.Sum(r => r.Weight);
            if (totalWeight <= 0) totalWeight = 1.0m;

            foreach (var offer in pricing.RestaurantOffers)
            {
                var concession = offer.RestaurantBasePrice * offer.RestaurantContributionPct;
                var commission = offer.RestaurantBasePrice * offer.CommissionRate;
                var netPayout = offer.RestaurantBasePrice - concession - commission;

                var normalizedWeight = offer.Weight / totalWeight;
                durationExpectedPayout += netPayout * normalizedWeight;

                if (netPayout > durationWorstCasePayout)
                {
                    durationWorstCasePayout = netPayout;
                }
            }

            var expectedNetProfit = customerPrice - durationExpectedPayout;
            var expectedMarginPct = expectedNetProfit / customerPrice;

            var worstCaseNetProfit = customerPrice - durationWorstCasePayout;
            var worstCaseMarginPct = worstCaseNetProfit / customerPrice;

            if (worstCaseMarginPct < globalWorstCaseMargin)
            {
                globalWorstCaseMargin = worstCaseMarginPct;
            }

            totalWeightedProfit += expectedNetProfit;
            totalWeightedCustomerPrice += customerPrice;

            durationDetails.Add(new DurationMarginDetail(
                pricing.DurationDays,
                customerPrice,
                durationExpectedPayout,
                expectedNetProfit,
                expectedMarginPct,
                worstCaseMarginPct));
        }

        var overallExpectedMarginPct = totalWeightedCustomerPrice > 0
            ? totalWeightedProfit / totalWeightedCustomerPrice
            : 0m;

        if (globalWorstCaseMargin == decimal.MaxValue)
        {
            globalWorstCaseMargin = 0m;
        }

        var isViable = overallExpectedMarginPct >= 0m;
        var requiresOverride = overallExpectedMarginPct < minimumExpectedMarginThreshold;

        return new CampaignProfitabilitySnapshot
        {
            CampaignId = campaignId,
            ProfitabilityVersion = profitabilityVersion,
            MinimumExpectedMarginPct = minimumExpectedMarginThreshold,
            OverallExpectedMarginPct = overallExpectedMarginPct,
            OverallWorstCaseMarginPct = globalWorstCaseMargin,
            IsViable = isViable,
            RequiresOverride = requiresOverride,
            DetailsJson = JsonSerializer.Serialize(durationDetails),
            EvaluatedAtUtc = DateTime.UtcNow
        };
    }
}
