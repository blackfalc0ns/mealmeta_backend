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
        Assert.Equal("NONE", result.Value.Allergens[0].Code);
        Assert.Equal("لا توجد حساسية", result.Value.Allergens[0].NameAr);
        Assert.False(result.Value.Allergens[0].IsSelected);
        Assert.True(result.Value.Allergens[0].IsNone);
        var milk = Assert.Single(result.Value.Allergens, x => x.Code != "NONE");
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
        Assert.Equal("NONE", result.Value.Ingredients[0].Code);
        Assert.Equal("أتناول كل أنواع الطعام", result.Value.Ingredients[0].NameAr);
        Assert.False(result.Value.Ingredients[0].IsSelected);
        Assert.True(result.Value.Ingredients[0].IsNone);
        var ingredient = Assert.Single(result.Value.Ingredients, x => x.Code != "NONE");
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
        Assert.Equal("NONE", result.Value.DietaryPreferences[0].Code);
        Assert.False(result.Value.DietaryPreferences[0].IsSelected);
        Assert.True(result.Value.DietaryPreferences[0].IsNone);
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
        var ingredient = Assert.Single(result.Value.Ingredients, x => x.Code != "NONE");
        Assert.Equal("دجاج", ingredient.NameAr);
        Assert.True(ingredient.IsSelected);
        Assert.Equal("NONE", result.Value.Ingredients[0].Code);
    }

    [Fact]
    public async Task Customer_without_preferences_reports_no_allergies()
    {
        var fixture = await SeedAsync(seedPreferences: false);
        var result = await fixture.Handler.Handle(new GetCustomerAllergiesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.SelectedCount);
        Assert.True(result.Value.HasNoAllergies);
        Assert.True(result.Value.Allergens[0].IsNone);
        Assert.Equal("NONE", result.Value.Allergens[0].Code);
        Assert.Equal("لا توجد حساسية", result.Value.Allergens[0].NameAr);
        Assert.True(result.Value.Allergens[0].IsSelected);
        Assert.DoesNotContain(result.Value.Allergens.Skip(1), x => x.IsSelected);
    }

    [Fact]
    public async Task Customer_without_preferences_reports_eats_everything()
    {
        var fixture = await SeedAsync(seedPreferences: false);
        var result = await fixture.Handler.Handle(new GetCustomerDislikedIngredientsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.SelectedCount);
        Assert.True(result.Value.EatsEverything);
        Assert.True(result.Value.Ingredients[0].IsNone);
        Assert.Equal("NONE", result.Value.Ingredients[0].Code);
        Assert.Equal("أتناول كل أنواع الطعام", result.Value.Ingredients[0].NameAr);
        Assert.True(result.Value.Ingredients[0].IsSelected);
        Assert.DoesNotContain(result.Value.Ingredients.Skip(1), x => x.IsSelected);
    }

    [Fact]
    public async Task Customer_without_preferences_reports_no_dietary_restriction()
    {
        var fixture = await SeedAsync(seedPreferences: false);
        var result = await fixture.Handler.Handle(new GetCustomerDietaryPreferencesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.SelectedCount);
        Assert.True(result.Value.HasNoDietaryRestriction);
        Assert.True(result.Value.DietaryPreferences[0].IsNone);
        Assert.Equal("NONE", result.Value.DietaryPreferences[0].Code);
        Assert.Equal("لا أتبع نظاماً غذائياً معيناً", result.Value.DietaryPreferences[0].NameAr);
        Assert.True(result.Value.DietaryPreferences[0].IsSelected);
        Assert.DoesNotContain(result.Value.DietaryPreferences.Skip(1), x => x.IsSelected);
    }

    [Fact]
    public async Task Disliked_ingredients_paginates_and_keeps_none_option()
    {
        var fixture = await SeedAsync(extraIngredients:
        [
            ("أرز", "Rice"),
            ("طماطم", "Tomato"),
            ("لحم", "Beef")
        ]);
        var page1 = await fixture.Handler.Handle(new GetCustomerDislikedIngredientsQuery(Page: 1, PageSize: 2), CancellationToken.None);
        var page2 = await fixture.Handler.Handle(new GetCustomerDislikedIngredientsQuery(Page: 2, PageSize: 2), CancellationToken.None);

        Assert.True(page1.IsSuccess);
        Assert.Equal("NONE", page1.Value.Ingredients[0].Code);
        Assert.Equal("أتناول كل أنواع الطعام", page1.Value.Ingredients[0].NameAr);
        Assert.Equal(3, page1.Value.Ingredients.Count);
        Assert.Equal(4, page1.Value.TotalCount);
        Assert.Equal(2, page1.Value.TotalPages);
        Assert.True(page1.Value.HasNextPage);
        Assert.False(page1.Value.HasPreviousPage);
        Assert.True(page1.Value.Ingredients[0].IsNone);
        Assert.False(page1.Value.Ingredients[0].IsSelected);

        Assert.True(page2.IsSuccess);
        Assert.Equal(2, page2.Value.Ingredients.Count);
        Assert.DoesNotContain(page2.Value.Ingredients, x => x.Code == "NONE");
        Assert.True(page2.Value.HasPreviousPage);
        Assert.False(page2.Value.HasNextPage);
        Assert.DoesNotContain(page2.Value.Ingredients.Select(x => x.Code), code => page1.Value.Ingredients.Skip(1).Select(x => x.Code).Contains(code));
    }

    [Fact]
    public async Task Disliked_ingredients_search_filters_by_name()
    {
        var fixture = await SeedAsync(extraIngredients: [("طماطم", "Tomato")]);
        var result = await fixture.Handler.Handle(new GetCustomerDislikedIngredientsQuery(Search: "دجاج"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("NONE", result.Value.Ingredients[0].Code);
        var ingredient = Assert.Single(result.Value.Ingredients, x => x.Code != "NONE");
        Assert.Equal("دجاج", ingredient.NameAr);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal("أتناول كل أنواع الطعام", result.Value.Ingredients[0].NameAr);
    }

    [Fact]
    public async Task Save_allergies_none_marks_allergies_completed()
    {
        var fixture = await SeedAsync(seedPreferences: false);
        var now = new DateTime(2026, 8, 18, 10, 0, 0, DateTimeKind.Utc);
        fixture.Clock.UtcNow.Returns(now);

        var result = await fixture.Handler.Handle(
            new SaveCustomerAllergiesCommand([new FoodPreferenceItemDto("NONE", null, null, null)]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var profile = await fixture.Db.CustomerProfiles.SingleAsync(x => x.UserId == fixture.CustomerId);
        Assert.Equal(now, profile.AllergiesCompletedAtUtc);
        Assert.Null(profile.DietaryPreferencesCompletedAtUtc);
        Assert.Empty(result.Value.Allergies);
    }

    [Fact]
    public async Task Save_dietary_none_marks_dietary_completed()
    {
        var fixture = await SeedAsync(seedPreferences: false);
        var now = new DateTime(2026, 8, 18, 10, 5, 0, DateTimeKind.Utc);
        fixture.Clock.UtcNow.Returns(now);

        var result = await fixture.Handler.Handle(
            new SaveCustomerDietaryPreferencesCommand(["NONE"]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var profile = await fixture.Db.CustomerProfiles.SingleAsync(x => x.UserId == fixture.CustomerId);
        Assert.Equal(now, profile.DietaryPreferencesCompletedAtUtc);
        Assert.Null(profile.AllergiesCompletedAtUtc);
        Assert.Empty(result.Value.DietaryPreferenceCodes ?? []);
    }

    [Fact]
    public async Task Save_food_preferences_marks_allergies_and_dietary_completed()
    {
        var fixture = await SeedAsync(seedPreferences: false);
        var now = new DateTime(2026, 8, 18, 10, 10, 0, DateTimeKind.Utc);
        fixture.Clock.UtcNow.Returns(now);

        var result = await fixture.Handler.Handle(
            new SaveCustomerFoodPreferencesCommand(null, [new FoodPreferenceItemDto("MILK", null, null, "High")], [], null, ["KETO"]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var profile = await fixture.Db.CustomerProfiles.SingleAsync(x => x.UserId == fixture.CustomerId);
        Assert.Equal(now, profile.AllergiesCompletedAtUtc);
        Assert.Equal(now, profile.DietaryPreferencesCompletedAtUtc);
    }

    [Fact]
    public async Task Unsaved_preferences_remain_incomplete()
    {
        var fixture = await SeedAsync(seedPreferences: false);
        var profile = await fixture.Db.CustomerProfiles.SingleAsync(x => x.UserId == fixture.CustomerId);
        Assert.Null(profile.AllergiesCompletedAtUtc);
        Assert.Null(profile.DietaryPreferencesCompletedAtUtc);
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

    private static async Task<Fixture> SeedAsync(UserType userType = UserType.Customer, bool secondChicken = false, bool seedPreferences = true, IReadOnlyList<(string Ar, string En)>? extraIngredients = null)
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
        if (extraIngredients is not null)
        {
            foreach (var (nameAr, nameEn) in extraIngredients)
            {
                db.Add(new Ingredient
                {
                    RestaurantId = restaurant.Id,
                    Code = nameEn.ToUpperInvariant(),
                    NameAr = nameAr,
                    NameEn = nameEn,
                    Category = "other",
                    IsActive = true
                });
            }
        }
        await db.SaveChangesAsync();

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(customerId);
        user.UserType.Returns(userType);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        return new Fixture(
            new CustomerFoodPreferencesHandlers(db, user, Substitute.For<IAuditLogger>(), clock),
            ingredient.Id,
            db,
            customerId,
            clock);
    }

    private sealed record Fixture(
        CustomerFoodPreferencesHandlers Handler,
        Guid IngredientId,
        AppDbContext Db,
        Guid CustomerId,
        IDateTimeProvider Clock);
}
