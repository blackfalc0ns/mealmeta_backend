using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.RestaurantMeals;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantMeals;

public class RestaurantMealsTests
{
    [Fact]
    public void CreateMealValidator_Rejects_Invalid_Nutrition_Bounds()
    {
        var command = new CreateMealCommand(
            Guid.NewGuid(), Guid.NewGuid(), "MEAL-BOUND", "وجبة", "Meal", Guid.NewGuid(),
            Calories: 500, ProteinG: 20, CarbsG: 30, FatG: 10,
            ServingWeightG: 0, NetCarbsG: 40, TotalSugarsG: 5, AddedSugarsG: 8);

        var result = new CreateMealCommandValidator().Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateMealCommand.ServingWeightG));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateMealCommand.NetCarbsG));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateMealCommand.AddedSugarsG));
    }

    [Fact]
    public void MealDto_Mapping_Preserves_Template_Fields()
    {
        var meal = new Meal
        {
            Code = "MEAL-TEMPLATE",
            NameAr = "وجبة",
            NameEn = "Meal",
            ServingWeightG = 350,
            FiberG = 8,
            NetCarbsG = 22,
            SugarAlcoholG = 3,
            SugarAlcoholType = "erythritol",
            SaturatedFatG = 4,
            TotalSugarsG = 12,
            AddedSugarsG = 2,
            SodiumMg = 420,
            AllergensReviewed = true,
            PresentAllergenCodes = "milk;gluten",
            MayContainAllergenCodes = "nuts",
            CrossContactNotes = "Shared fryer",
            DataResponsibilityConfirmed = true,
            NutritionTagCodes = "high-protein;low-sodium",
            SearchExclusionTagCodes = "no-dairy",
        };

        var dto = MealHandlers.MapToDto(meal);

        Assert.Equal(350, dto.ServingWeightG);
        Assert.Equal(22, dto.NetCarbsG);
        Assert.Equal("milk;gluten", dto.PresentAllergenCodes);
        Assert.True(dto.AllergensReviewed);
        Assert.True(dto.DataResponsibilityConfirmed);
        Assert.Equal("high-protein;low-sodium", dto.NutritionTagCodes);
    }
    [Fact]
    public void MealDto_Mapping_Calculates_Nutritional_Values_Correctly()
    {
        var ingredient1 = new Ingredient
        {
            Code = "ING-01",
            NameAr = "دجاج",
            NameEn = "Chicken",
            DefaultCaloriesPer100g = 165,
            DefaultProteinPer100g = 31,
            DefaultCarbsPer100g = 0,
            DefaultFatPer100g = 3.6m
        };
        var meal = new Meal
        {
            MenuId = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            Code = "MEAL-01",
            NameAr = "وجبة دجاج مشوي",
            NameEn = "Grilled Chicken Meal",
            IsAvailableForNewOrders = true,
            CreatedAtUtc = DateTime.UtcNow,
            Ingredients = new List<MealIngredient>
            {
                new MealIngredient
                {
                    IngredientId = ingredient1.Id,
                    Ingredient = ingredient1,
                    PortionGrams = 200,
                    IsOptional = false,
                    SortOrder = 1
                }
            }
        };

        var dto = MealHandlers.MapToDto(meal);

        Assert.Equal("MEAL-01", dto.Code);
        Assert.Single(dto.Ingredients);

        var ingDto = dto.Ingredients[0];
        Assert.Equal("ING-01", ingDto.IngredientCode);
        Assert.Equal(200, ingDto.PortionGrams);
        Assert.Equal(330.0m, ingDto.CalculatedCalories); // 165 * 2
        Assert.Equal(62.0m, ingDto.CalculatedProteinG);  // 31 * 2
        Assert.Equal(7.2m, ingDto.CalculatedFatG);        // 3.6 * 2
    }

    [Fact]
    public void MenuDto_Mapping_Returns_Correct_MealCount()
    {
        var menu = new Menu
        {
            RestaurantId = Guid.NewGuid(),
            NameAr = "قائمة الغداء",
            NameEn = "Lunch Menu",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var dto = MenuHandlers.MapToDto(menu, 5);

        Assert.Equal("Lunch Menu", dto.NameEn);
        Assert.Equal(5, dto.MealCount);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public void MealDto_Mapping_Includes_ApprovalStatus_Defaults()
    {
        var meal = new Meal
        {
            MenuId = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            Code = "MEAL-02",
            NameAr = "وجبة صحية",
            NameEn = "Healthy Meal",
            ApprovalStatus = MealMate.Domain.Enums.VerificationStatus.Submitted
        };

        var dto = MealHandlers.MapToDto(meal);

        Assert.Equal(MealMate.Domain.Enums.VerificationStatus.Submitted, dto.ApprovalStatus);
        Assert.Null(dto.RejectionReason);
    }

    [Fact]
    public async Task MealPrograms_Returns_Only_Selected_Programs_When_Offers_Exist()
    {
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var selected = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "SELECTED",
            NameAr = "مختار",
            NameEn = "Selected",
            IsActive = true,
        };
        var selectedBundle = new Bundle
        {
            ProgramId = selected.Id,
            Program = selected,
            Code = "SELECTED-BOX",
            NameAr = "باقة مختارة",
            NameEn = "Selected box",
            MealsPerDay = 3,
            IsActive = true,
        };
        var other = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "OTHER",
            NameAr = "آخر",
            NameEn = "Other",
            IsActive = true,
        };
        db.Programs.AddRange(selected, other);
        db.Bundles.Add(selectedBundle);
        db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            RestaurantId = restaurantId,
            ProgramId = selected.Id,
            BundleId = selectedBundle.Id,
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var result = await new MenuHandlers(db).Handle(
            new GetRestaurantMealProgramsQuery(restaurantId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(selected.Id, result.Value[0].Id);
        Assert.Single(result.Value[0].Bundles);
        Assert.Equal(selectedBundle.Id, result.Value[0].Bundles[0].Id);
    }

    [Fact]
    public async Task MealPrograms_Returns_Active_Catalog_For_Legacy_Restaurant()
    {
        await using var db = CreateDb();
        var active = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "ACTIVE",
            NameAr = "فعال",
            NameEn = "Active",
            IsActive = true,
        };
        var activeBundle = new Bundle
        {
            ProgramId = active.Id,
            Program = active,
            Code = "ACTIVE-BOX",
            NameAr = "باقة فعالة",
            NameEn = "Active box",
            MealsPerDay = 2,
            IsActive = true,
        };
        var inactive = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "INACTIVE",
            NameAr = "غير فعال",
            NameEn = "Inactive",
            IsActive = false,
        };
        db.Programs.AddRange(active, inactive);
        db.Bundles.Add(activeBundle);
        await db.SaveChangesAsync();

        var result = await new MenuHandlers(db).Handle(
            new GetRestaurantMealProgramsQuery(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(active.Id, result.Value[0].Id);
        Assert.Single(result.Value[0].Bundles);
    }

    private static Persistence.AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Persistence.AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Persistence.AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }
}
