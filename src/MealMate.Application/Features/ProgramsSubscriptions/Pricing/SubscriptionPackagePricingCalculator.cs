using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions.Pricing;

public sealed record SubscriptionTierPriceAverage(
    RestaurantTier Tier,
    int RestaurantCount,
    decimal AveragePrice26DaysKwd,
    decimal AverageDailyPriceKwd);

public static class SubscriptionPackagePricingCalculator
{
    public const decimal DefaultDailyPriceKwd = 3.500m;

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
        var standardDeviation = (decimal)Math.Sqrt((double)variance);

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
                Math.Round(group.Average(item => item.Price26), 3),
                Math.Round(group.Average(item => item.DailyPrice), 6)))
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

        var restaurantCount = averages.Sum(item => item.RestaurantCount);
        return restaurantCount > 0
            ? averages.Sum(item => item.AverageDailyPriceKwd * item.RestaurantCount) / restaurantCount
            : DefaultDailyPriceKwd;
    }

    private static RestaurantTier ResolveTier(
        decimal dailyPrice,
        int restaurantCount,
        decimal mean,
        decimal standardDeviation)
    {
        if (restaurantCount == 1)
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
