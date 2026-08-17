using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.IdentityAccess;

public sealed class CustomerFoodPreferencesTests
{
    [Fact]
    public async Task Get_allergies_marks_selected_definitions_only()
    {
        var fixture = await SeedAsync();
        var result = await fixture.Handler.Handle(new GetCustomerAllergiesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.SelectedCount);
        var milk = Assert.Single(result.Value.Allergens);
        Assert.Equal("MILK", milk.Code);
        Assert.True(milk.IsSelected);
        Assert.Equal("High", milk.Severity);
        Assert.Equal("الحليب", milk.NameAr);
    }

    [Fact]
    public async Task Get_disliked_ingredients_marks_selected_ingredient()
    {
        var fixture = await SeedAsync();
        var result = await fixture.Handler.Handle(new GetCustomerDislikedIngredientsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.SelectedCount);
        var ingredient = Assert.Single(result.Value.Ingredients);
        Assert.Equal($"INGREDIENT:{fixture.IngredientId}", ingredient.Code);
        Assert.True(ingredient.IsSelected);
        Assert.Equal("دجاج", ingredient.NameAr);
        Assert.Equal(["MILK"], ingredient.AllergenCodes);
    }

    [Fact]
    public async Task Get_dietary_marks_selected_programs_only()
    {
        var fixture = await SeedAsync();
        var result = await fixture.Handler.Handle(new GetCustomerDietaryPreferencesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.SelectedCount);
        Assert.True(result.Value.DietaryPreferences.Single(x => x.Code == "KETO").IsSelected);
        Assert.False(result.Value.DietaryPreferences.Single(x => x.Code == "VEGETARIAN").IsSelected);
    }

    [Fact]
    public async Task Catalog_sets_ingredient_is_selected()
    {
        var fixture = await SeedAsync();
        var result = await fixture.Handler.Handle(new GetCustomerFoodPreferenceCatalogQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(result.Value.Ingredients).IsSelected);
        Assert.Equal(1, result.Value.SelectedAllergenCount);
    }

    [Fact]
    public async Task Catalog_hides_allergens_not_present_on_restaurant_ingredients()
    {
        var fixture = await SeedAsync();
        var result = await fixture.Handler.Handle(new GetCustomerFoodPreferenceCatalogQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(result.Value.Allergens, x => x.Code == "GLUTEN");
        Assert.Contains(result.Value.Allergens, x => x.Code == "MILK");
    }

    [Fact]
    public async Task Catalog_dedupes_disliked_ingredients_by_name()
    {
        var fixture = await SeedAsync(secondChicken: true);
        var result = await fixture.Handler.Handle(new GetCustomerDislikedIngredientsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var ingredient = Assert.Single(result.Value.Ingredients);
        Assert.Equal("دجاج", ingredient.NameAr);
        Assert.True(ingredient.IsSelected);
    }

    [Fact]
    public async Task Customer_without_preferences_reports_no_allergies()
    {
        var fixture = await SeedAsync(seedPreferences: false);
        var result = await fixture.Handler.Handle(new GetCustomerAllergiesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.SelectedCount);
        Assert.True(result.Value.HasNoAllergies);
        Assert.DoesNotContain(result.Value.Allergens, x => x.IsSelected);
    }

    [Fact]
    public async Task Customer_without_preferences_reports_eats_everything()
    {
        var fixture = await SeedAsync(seedPreferences: false);
        var result = await fixture.Handler.Handle(new GetCustomerDislikedIngredientsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.SelectedCount);
        Assert.True(result.Value.EatsEverything);
        Assert.DoesNotContain(result.Value.Ingredients, x => x.IsSelected);
    }

    [Fact]
    public async Task Customer_without_preferences_reports_no_dietary_restriction()
    {
        var fixture = await SeedAsync(seedPreferences: false);
        var result = await fixture.Handler.Handle(new GetCustomerDietaryPreferencesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.SelectedCount);
        Assert.True(result.Value.HasNoDietaryRestriction);
        Assert.DoesNotContain(result.Value.DietaryPreferences, x => x.IsSelected);
    }

    [Fact]
    public async Task Save_allergies_rejects_unknown_code()
    {
        var fixture = await SeedAsync();
        var result = await fixture.Handler.Handle(
            new SaveCustomerAllergiesCommand([new FoodPreferenceItemDto("NOT_AN_ALLERGEN", null, null, null)]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("food_preferences.invalid_allergen", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task Get_allergies_rejects_non_customer()
    {
        var fixture = await SeedAsync(UserType.Restaurant);
        var result = await fixture.Handler.Handle(new GetCustomerAllergiesQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
    }

    private static async Task<Fixture> SeedAsync(UserType userType = UserType.Customer, bool secondChicken = false, bool seedPreferences = true)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var customerId = Guid.NewGuid();
        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            LegalNameAr = "مطعم",
            LegalNameEn = "Restaurant",
            TradeNameAr = "مطعم",
            TradeNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true
        };
        var ingredient = new Ingredient
        {
            RestaurantId = restaurant.Id,
            Code = "CHICKEN",
            NameAr = "دجاج",
            NameEn = "Chicken",
            Category = "poultry",
            AllergenCodes = "MILK",
            IsActive = true
        };
        db.AddRange(
            new CustomerProfile { UserId = customerId },
            restaurant,
            ingredient,
            new AllergenDefinition { Code = "GLUTEN", NameAr = "القمح والجلوتين", NameEn = "Wheat and gluten", IsActive = true, SortOrder = 1 },
            new AllergenDefinition { Code = "MILK", NameAr = "الحليب", NameEn = "Milk", IsActive = true, SortOrder = 2 },
            new FoodPreferenceDefinition { Code = "KETO", NameAr = "كيتو", NameEn = "Keto", IsActive = true, SortOrder = 1 },
            new FoodPreferenceDefinition { Code = "VEGETARIAN", NameAr = "نباتي", NameEn = "Vegetarian", IsActive = true, SortOrder = 2 });
        if (seedPreferences)
        {
            db.AddRange(
                new CustomerAllergyPreference
                {
                    CustomerUserId = customerId,
                    AllergenCode = "MILK",
                    AllergenNameAr = "الحليب",
                    AllergenNameEn = "Milk",
                    Severity = "High",
                    IsDislike = false
                },
                new CustomerAllergyPreference
                {
                    CustomerUserId = customerId,
                    AllergenCode = $"INGREDIENT:{ingredient.Id}",
                    AllergenNameAr = "دجاج",
                    AllergenNameEn = "Chicken",
                    IsDislike = true
                },
                new CustomerDietaryPreference { CustomerUserId = customerId, PreferenceCode = "KETO" });
        }
        if (secondChicken)
        {
            var otherRestaurant = new RestaurantProfile
            {
                UserId = Guid.NewGuid(),
                LegalNameAr = "مطعم ٢",
                LegalNameEn = "Restaurant 2",
                TradeNameAr = "مطعم ٢",
                TradeNameEn = "Restaurant 2",
                VerificationStatus = VerificationStatus.Approved,
                IsOperationallyActive = true
            };
            db.AddRange(otherRestaurant, new Ingredient
            {
                RestaurantId = otherRestaurant.Id,
                Code = "CHICKEN-2",
                NameAr = "دجاج",
                NameEn = "Chicken",
                Category = "poultry",
                AllergenCodes = "MILK",
                IsActive = true
            });
        }
        await db.SaveChangesAsync();

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(customerId);
        user.UserType.Returns(userType);
        return new Fixture(
            new CustomerFoodPreferencesHandlers(db, user, Substitute.For<IAuditLogger>()),
            ingredient.Id);
    }

    private sealed record Fixture(CustomerFoodPreferencesHandlers Handler, Guid IngredientId);
}
