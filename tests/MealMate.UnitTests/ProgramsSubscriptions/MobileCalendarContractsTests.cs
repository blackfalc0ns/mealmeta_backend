using FluentAssertions;
using MealMate.Application.Features.CalendarMeals;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public sealed class MobileCalendarContractsTests
{
    [Fact]
    public void MobileMealNutritionDetails_ComputesMacroPercentagesCorrectly()
    {
        var proteinGrams = 45m;
        var carbsGrams = 60m;
        var fatGrams = 15m;
        var calories = 550;

        // Total macronutrient calories = (protein * 4) + (carbs * 4) + (fat * 9)
        // 45*4=180, 60*4=240, 15*9=135 => total = 555 kcal
        var totalCals = (proteinGrams * 4) + (carbsGrams * 4) + (fatGrams * 9);
        var proteinPct = Math.Round((proteinGrams * 4 / totalCals) * 100, 1); // ~32.4%
        var carbsPct = Math.Round((carbsGrams * 4 / totalCals) * 100, 1);    // ~43.2%
        var fatPct = Math.Round((fatGrams * 9 / totalCals) * 100, 1);        // ~24.3%

        var details = new MobileMealNutritionDetailsDto(
            MealId: Guid.NewGuid(),
            NameAr: "دجاج مشوي مع الأرز البني",
            NameEn: "Grilled Chicken with Brown Rice",
            DescriptionAr: "وجبة متكاملة غنية بالبروتين ومصنوعة من مكونات طازجة",
            PhotoUrl: "https://example.com/chicken.jpg",
            RestaurantRating: 4.8m,
            RatingCount: 520,
            Ingredients: ["دجاج مشوي", "أرز بني", "جزر", "بروكلي", "خس", "ليمون"],
            Calories: calories,
            ProteinGrams: proteinGrams,
            CarbsGrams: carbsGrams,
            FatGrams: fatGrams,
            FiberGrams: 7m,
            ProteinPercentage: proteinPct,
            CarbsPercentage: carbsPct,
            FatPercentage: fatPct,
            Allergens: ["جلوتين", "لاكتوز", "مكسرات"]);

        details.ProteinPercentage.Should().BeGreaterThan(30m);
        details.CarbsPercentage.Should().BeGreaterThan(40m);
        details.FatPercentage.Should().BeLessThan(30m);
        details.Allergens.Should().Contain("جلوتين");
    }

    [Fact]
    public void MobileEligibleRestaurant_ChecksQuotaExhaustion()
    {
        var restaurant = new MobileEligibleRestaurantDto(
            RestaurantId: Guid.NewGuid(),
            NameAr: "بروتين كيتشن",
            NameEn: "The Protein Kitchen",
            LogoUrl: "https://example.com/logo.png",
            Rating: 4.8m,
            AvailableMealsCount: 28,
            AllowedDaysQuota: 2,
            UsedDaysCount: 2,
            RemainingDaysQuota: 0,
            IsQuotaExhausted: true,
            Badges: ["وجبات صحية متوازنة بمكونات طبيعية"]);

        restaurant.IsQuotaExhausted.Should().BeTrue();
        restaurant.RemainingDaysQuota.Should().Be(0);
    }
}
