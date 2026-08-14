using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.MenuCycles;
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
    public void AutoCalculate_Sets_ServingWeight_Macros_NetCarbs_And_PresentAllergens()
    {
        var chicken = new Ingredient
        {
            Code = "ING-CHICKEN",
            NameAr = "دجاج",
            NameEn = "Chicken",
            DefaultCaloriesPer100g = 165,
            DefaultProteinPer100g = 31,
            DefaultCarbsPer100g = 10,
            DefaultFatPer100g = 3.6m,
            AllergenCodes = "MILK",
        };
        var meal = new Meal
        {
            Code = "MEAL-CALC",
            NameAr = "وجبة",
            NameEn = "Meal",
            Calories = 1,
            ServingWeightG = 10,
            FiberG = 5,
            SugarAlcoholG = 2,
            Ingredients =
            {
                new MealIngredient
                {
                    Ingredient = chicken,
                    IngredientId = chicken.Id,
                    PortionGrams = 200,
                },
            },
        };

        MealHandlers.AutoCalculateNutritionalValues(meal);

        Assert.Equal(200, meal.ServingWeightG);
        Assert.Equal(330.0m, meal.Calories);
        Assert.Equal(62.0m, meal.ProteinG);
        Assert.Equal(20.0m, meal.CarbsG);
        Assert.Equal(7.2m, meal.FatG);
        Assert.Equal(13.0m, meal.NetCarbsG);
        Assert.Equal("MILK", meal.PresentAllergenCodes);
        Assert.Equal("ingredient_recipe", meal.NutritionSource);
    }

    [Fact]
    public void AutoCalculate_Preserves_Custom_NutritionSource()
    {
        var ingredient = new Ingredient
        {
            Code = "ING-LAB",
            NameAr = "أرز",
            NameEn = "Rice",
            DefaultCaloriesPer100g = 130,
            DefaultCarbsPer100g = 28,
        };
        var meal = new Meal
        {
            Code = "MEAL-LAB",
            NameAr = "وجبة",
            NameEn = "Meal",
            NutritionSource = "lab",
            Ingredients =
            {
                new MealIngredient { Ingredient = ingredient, IngredientId = ingredient.Id, PortionGrams = 100 },
            },
        };

        MealHandlers.AutoCalculateNutritionalValues(meal);

        Assert.Equal("lab", meal.NutritionSource);
        Assert.Equal(130.0m, meal.Calories);
    }

    [Fact]
    public void AutoCalculate_Clears_Recipe_Fields_When_No_Ingredients()
    {
        var meal = new Meal
        {
            Code = "MEAL-EMPTY",
            NameAr = "وجبة",
            NameEn = "Meal",
            Calories = 400,
            ProteinG = 20,
            CarbsG = 30,
            FatG = 10,
            ServingWeightG = 350,
            NetCarbsG = 22,
            FiberG = 8,
            PresentAllergenCodes = "MILK",
            MayContainAllergenCodes = "NUTS",
        };

        MealHandlers.AutoCalculateNutritionalValues(meal);

        Assert.Null(meal.Calories);
        Assert.Null(meal.ProteinG);
        Assert.Null(meal.CarbsG);
        Assert.Null(meal.FatG);
        Assert.Null(meal.ServingWeightG);
        Assert.Null(meal.NetCarbsG);
        Assert.Null(meal.PresentAllergenCodes);
        Assert.Equal(8, meal.FiberG);
        Assert.Equal("NUTS", meal.MayContainAllergenCodes);
    }

    [Fact]
    public void AutoCalculate_Keeps_Restaurant_PresentAllergens_When_Ingredients_Have_None()
    {
        var rice = new Ingredient
        {
            Code = "ING-RICE",
            NameAr = "أرز",
            NameEn = "Rice",
            DefaultCaloriesPer100g = 130,
            DefaultCarbsPer100g = 28,
        };
        var meal = new Meal
        {
            Code = "MEAL-ALLERGEN",
            NameAr = "وجبة",
            NameEn = "Meal",
            PresentAllergenCodes = "GLUTEN,SESAME",
            Ingredients =
            {
                new MealIngredient { Ingredient = rice, IngredientId = rice.Id, PortionGrams = 100 },
            },
        };

        MealHandlers.AutoCalculateNutritionalValues(meal);

        Assert.Equal("GLUTEN,SESAME", meal.PresentAllergenCodes);
        Assert.Equal(130.0m, meal.Calories);
    }

    [Fact]
    public void SyncProgramAssignments_Reuses_Existing_Row_Including_Soft_Deleted()
    {
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var existing = new MealProgramAssignment
        {
            ProgramId = programId,
            BundleId = Guid.NewGuid(),
            IsDeleted = true,
            DeletedAtUtc = DateTime.UtcNow,
        };
        var meal = new Meal
        {
            Code = "MEAL-ASSIGN",
            NameAr = "وجبة",
            NameEn = "Meal",
            ProgramAssignments = { existing },
        };

        MealHandlers.SyncProgramAssignments(meal, [new MealProgramAssignmentInput(programId, bundleId)]);

        Assert.Single(meal.ProgramAssignments);
        Assert.Same(existing, meal.ProgramAssignments.Single());
        Assert.False(existing.IsDeleted);
        Assert.Null(existing.DeletedAtUtc);
        Assert.Equal(bundleId, existing.BundleId);
        Assert.Equal(programId, meal.ProgramId);
        Assert.Equal(bundleId, meal.BundleId);
    }

    [Fact]
    public void ResolveSaveApprovalStatus_Keeps_Draft_When_Nutrition_Is_Out_Of_Range()
    {
        var meal = new Meal
        {
            Code = "MEAL-GATE",
            NameAr = "وجبة",
            NameEn = "Meal",
            DescriptionAr = "وصف",
            DescriptionEn = "Description",
            Calories = 900,
            ProteinG = 40,
            CarbsG = 80,
            FatG = 30,
            AllergensReviewed = true,
            DataResponsibilityConfirmed = true,
            Images = { new MealImage { ImageUrl = "https://example.test/meal.jpg" } },
            Ingredients = { new MealIngredient { Ingredient = new Ingredient { IsActive = true, OriginCountryCode = "KW" }, PortionGrams = 100 } },
        };

        var status = MealHandlers.ResolveSaveApprovalStatus(meal, ["PROG|lunch: kcal 900 is outside 300-600."]);

        Assert.Equal(MealMate.Domain.Enums.VerificationStatus.Draft, status);
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
    public async Task MealPrograms_Returns_Empty_When_Restaurant_Has_No_Offers()
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
        db.Programs.Add(active);
        db.Bundles.Add(activeBundle);
        await db.SaveChangesAsync();

        var result = await new MenuHandlers(db).Handle(
            new GetRestaurantMealProgramsQuery(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    private static Persistence.AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Persistence.AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Persistence.AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }
}
