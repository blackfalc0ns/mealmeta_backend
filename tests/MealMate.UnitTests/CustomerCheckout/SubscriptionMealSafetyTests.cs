using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using MealMate.Application.Common.Abstractions;
using Xunit;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class SubscriptionMealSafetyTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task Requested_program_requires_explicit_membership_and_its_own_nutrition_standard(
        bool explicitlyAssigned, bool meetsRequestedStandard, bool expectedSafe)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var own = new Program { Code = "WEIGHT", NameAr = "وزن", NameEn = "Weight", IsActive = true, Status = ProgramStatus.Active };
        var requested = new Program { Code = "MUSCLE", NameAr = "عضلات", NameEn = "Muscle", IsActive = true, Status = ProgramStatus.Active };
        var restaurant = new RestaurantProfile { UserId = Guid.NewGuid(), LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            TradeNameAr = "مطعم", TradeNameEn = "Restaurant", VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true };
        var menu = new Menu { RestaurantId = restaurant.Id, ProgramId = own.Id, NameAr = "قائمة", NameEn = "Menu", IsActive = true };
        var ingredient = new Ingredient { RestaurantId = restaurant.Id, Code = "ING", NameAr = "مكون", NameEn = "Ingredient", OriginCountryCode = "KW", IsActive = true };
        var meal = new Meal { RestaurantId = restaurant.Id, MenuId = menu.Id, Menu = menu, ProgramId = own.Id,
            Code = "MEAL", NameAr = "وجبة", NameEn = "Meal", Slot = "lunch", ApprovalStatus = VerificationStatus.Approved,
            IsAvailableForNewOrders = true, AllergensReviewed = true, DataResponsibilityConfirmed = true,
            Calories = 500, ProteinG = 40, CarbsG = 50, NetCarbsG = 50, FatG = 20, FiberG = 8 };
        db.AddRange(own, requested, restaurant, menu, ingredient, meal,
            new MealIngredient { MealId = meal.Id, IngredientId = ingredient.Id, PortionGrams = 100 },
            new MealImage { MealId = meal.Id, ImageUrl = "/meal.png", IsPrimary = true },
            Standard(own.Id, 400, 600), Standard(requested.Id, meetsRequestedStandard ? 400 : 650, 850));
        if (explicitlyAssigned) db.Add(new MealProgramAssignment { MealId = meal.Id, ProgramId = requested.Id });
        await db.SaveChangesAsync();

        var safe = await SubscriptionMealSafety.ResolveAsync(db, [meal.Id], requested.Id,
            MealPreferenceFilter.Empty, CancellationToken.None);

        Assert.Equal(expectedSafe, safe.Contains(meal.Id));
    }

    private static ProgramNutritionStandard Standard(Guid programId, decimal kcalMin, decimal kcalMax)
        => new() { ProgramId = programId, MealCategory = "MAIN", KcalMin = kcalMin, KcalMax = kcalMax,
            ProteinGMin = 30, ProteinGMax = 50, CarbsGMin = 40, CarbsGMax = 60, FatGMin = 15, FatGMax = 25, FiberGMin = 5 };
}
