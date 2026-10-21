using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions.Pricing;

public sealed record SubscriptionTierPriceAverage(
    RestaurantTier Tier,
    int RestaurantCount,
    decimal AveragePrice26DaysKwd,
    decimal AverageDailyPriceKwd);

public static class SubscriptionPackagePricingCalculator
{
    public static IReadOnlyList<SubscriptionTierPriceAverage> CalculateTierAverages(
        IEnumerable<decimal> activePrices26DaysKwd)
    {
        var prices = activePrices26DaysKwd.Where(price => price > 0m).ToList();
        if (prices.Count == 0) return [];

        var dailyPrices = prices.Select(price => price / 26m).ToList();
        var mean = dailyPrices.Average();
        var variance = dailyPrices.Count < 2
            ? 0m
            : dailyPrices.Sum(value => (value - mean) * (value - mean)) / dailyPrices.Count;
        var standardDeviation = DecimalPricingMath.Sqrt(variance);

        return prices
            .Select(price => new
            {
                Price26 = price,
                DailyPrice = price / 26m,
                Tier = ResolveTier(price / 26m, prices.Count, mean, standardDeviation),
            })
            .GroupBy(item => item.Tier)
            .Select(group => new SubscriptionTierPriceAverage(
                group.Key,
                group.Count(),
                group.Average(item => item.Price26),
                group.Average(item => item.DailyPrice)))
            .OrderBy(item => item.Tier)
            .ToList();
    }

    public static decimal ResolveAverageDailyPrice(
        IEnumerable<decimal> activePrices26DaysKwd,
        RestaurantTier tier)
    {
        var averages = CalculateTierAverages(activePrices26DaysKwd);
        var requestedTierAverage = averages.FirstOrDefault(item => item.Tier == tier)?.AverageDailyPriceKwd;
        if (requestedTierAverage.HasValue) return requestedTierAverage.Value;
        if (averages.Count == 0)
            throw new InvalidOperationException("Pricing cannot be calculated without active restaurant pricing data.");
        throw new InvalidOperationException($"Pricing cannot be calculated because tier '{tier}' has no active restaurants.");
    }

    public static RestaurantTier ResolveTier(
        decimal dailyPrice,
        int restaurantCount,
        decimal mean,
        decimal standardDeviation)
    {
        if (restaurantCount <= 0) throw new ArgumentOutOfRangeException(nameof(restaurantCount));
        if (restaurantCount == 1 || standardDeviation <= 0m)
        {
            if (dailyPrice < 4.5m) return RestaurantTier.Basic;
            if (dailyPrice >= 6m) return RestaurantTier.Elite;
            return RestaurantTier.Platinum;
        }

        if (dailyPrice <= mean - (0.5m * standardDeviation)) return RestaurantTier.Basic;
        if (dailyPrice >= mean + (0.5m * standardDeviation)) return RestaurantTier.Elite;
        return RestaurantTier.Platinum;
    }
}
