using FluentAssertions;
using MealMate.Application.Common.Geography;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using MealMate.Persistence.Seeding;

namespace MealMate.UnitTests.Seeding;

public sealed class DemoRestaurantBrandCatalogTests
{
    [Fact]
    public void Demo_brands_span_kuwait_governorates_and_all_three_tiers()
    {
        var brands = DemoRestaurantBrandCatalog.All;

        brands.Should().HaveCountGreaterThanOrEqualTo(30);
        brands.Select(x => x.Email).Should().OnlyHaveUniqueItems();
        brands.Select(x => (x.Latitude, x.Longitude)).Should().OnlyHaveUniqueItems();
        brands.Select(x => x.GovernorateEn).Distinct().Should().HaveCountGreaterThanOrEqualTo(5);

        foreach (var brand in brands)
        {
            KuwaitGeoBounds.IsWithin(brand.Latitude, brand.Longitude).Should().BeTrue(because: brand.NameEn);
            brand.Price26.Should().BeGreaterThan(0m);
        }

        var dailies = brands.Select(x => x.Price26 / 26m).ToList();
        var mean = dailies.Average();
        var variance = dailies.Sum(value => (value - mean) * (value - mean)) / dailies.Count;
        var standardDeviation = (decimal)Math.Sqrt((double)variance);
        var tiers = dailies
            .Select(daily => SubscriptionPackagePricingCalculator.ResolveTier(daily, dailies.Count, mean, standardDeviation))
            .ToHashSet();

        tiers.Should().Contain(RestaurantTier.Basic);
        tiers.Should().Contain(RestaurantTier.Platinum);
        tiers.Should().Contain(RestaurantTier.Elite);
    }
}
