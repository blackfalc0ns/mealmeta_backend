using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.RestaurantMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantMeals;

public sealed class MealNotificationOutboxTests
{
    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static async Task<Meal> SeedReadyMealAsync(AppDbContext db, Guid restaurantId, VerificationStatus status = VerificationStatus.Draft)
    {
        var ingredient = new Ingredient
        {
            RestaurantId = restaurantId,
            Code = "ING-TEST",
            NameAr = "مكون",
            NameEn = "Ingredient",
            IsActive = true,
            OriginCountryCode = "KWT"
        };
        db.Ingredients.Add(ingredient);

        var menu = new Menu
        {
            RestaurantId = restaurantId,
            NameAr = "القائمة",
            NameEn = "Menu"
        };
        db.Menus.Add(menu);

        var meal = new Meal
        {
            RestaurantId = restaurantId,
            MenuId = menu.Id,
            Menu = menu,
            Code = "MEAL-TEST",
            NameAr = "وجبة اختبار",
            NameEn = "Test Meal",
            DescriptionAr = "وصف وجبة اختبار",
            DescriptionEn = "Test meal description",
            Calories = 450,
            ProteinG = 30,
            CarbsG = 40,
            FatG = 15,
            AllergensReviewed = true,
            DataResponsibilityConfirmed = true,
            ApprovalStatus = status
        };
        db.Meals.Add(meal);

        var mealImage = new MealImage
        {
            Meal = meal,
            MealId = meal.Id,
            ImageUrl = "https://cdn.mealmate.app/meals/test.jpg",
            IsDeleted = false
        };
        meal.Images.Add(mealImage);

        var mealIngredient = new MealIngredient
        {
            Meal = meal,
            MealId = meal.Id,
            Ingredient = ingredient,
            IngredientId = ingredient.Id,
            PortionGrams = 100,
            IsDeleted = false
        };
        meal.Ingredients.Add(mealIngredient);

        await db.SaveChangesAsync();
        return meal;
    }

    [Fact]
    public async Task SubmitMealCommand_WhenReady_Adds_MealSubmitted_OutboxMessage()
    {
        await using var db = CreateInMemoryDb();
        var restaurantId = Guid.NewGuid();
        var meal = await SeedReadyMealAsync(db, restaurantId, VerificationStatus.Draft);

        var userId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc));

        var handler = new MealHandlers(db, currentUser, clock);

        var result = await handler.Handle(new SubmitMealCommand(meal.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");

        var outbox = await db.OutboxMessages.FirstOrDefaultAsync(x => x.EventType == "MealSubmitted");
        outbox.Should().NotBeNull("Expected an OutboxMessage with EventType 'MealSubmitted'");
        outbox!.AggregateType.Should().Be("Meal");
        outbox.AggregateId.Should().Be(meal.Id.ToString());

        using var doc = JsonDocument.Parse(outbox.PayloadJson);
        var root = doc.RootElement;
        root.GetProperty("MealId").GetGuid().Should().Be(meal.Id);
        root.GetProperty("RestaurantId").GetGuid().Should().Be(restaurantId);
        root.GetProperty("MealNameAr").GetString().Should().Be(meal.NameAr);
        root.GetProperty("MealNameEn").GetString().Should().Be(meal.NameEn);
        root.GetProperty("SubmittedByUserId").GetGuid().Should().Be(userId);
    }

    [Fact]
    public async Task ApproveMealCommand_Adds_MealApproved_OutboxMessage()
    {
        await using var db = CreateInMemoryDb();
        var restaurantId = Guid.NewGuid();
        var meal = await SeedReadyMealAsync(db, restaurantId, VerificationStatus.Submitted);

        var adminUserId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(adminUserId);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc));

        var handler = new MealHandlers(db, currentUser, clock);

        var result = await handler.Handle(new ApproveMealCommand(meal.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");

        var outbox = await db.OutboxMessages.FirstOrDefaultAsync(x => x.EventType == "MealApproved");
        outbox.Should().NotBeNull("Expected an OutboxMessage with EventType 'MealApproved'");
        outbox!.AggregateType.Should().Be("Meal");
        outbox.AggregateId.Should().Be(meal.Id.ToString());

        using var doc = JsonDocument.Parse(outbox.PayloadJson);
        var root = doc.RootElement;
        root.GetProperty("MealId").GetGuid().Should().Be(meal.Id);
        root.GetProperty("RestaurantId").GetGuid().Should().Be(restaurantId);
        root.GetProperty("MealNameAr").GetString().Should().Be(meal.NameAr);
        root.GetProperty("MealNameEn").GetString().Should().Be(meal.NameEn);
        root.GetProperty("ReviewedByUserId").GetGuid().Should().Be(adminUserId);
    }

    [Fact]
    public async Task RejectMealCommand_Adds_MealRejected_OutboxMessage()
    {
        await using var db = CreateInMemoryDb();
        var restaurantId = Guid.NewGuid();
        var meal = await SeedReadyMealAsync(db, restaurantId, VerificationStatus.Submitted);

        var adminUserId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(adminUserId);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc));

        var handler = new MealHandlers(db, currentUser, clock);

        var result = await handler.Handle(new RejectMealCommand(meal.Id, "Inappropriate image"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");

        var outbox = await db.OutboxMessages.FirstOrDefaultAsync(x => x.EventType == "MealRejected");
        outbox.Should().NotBeNull("Expected an OutboxMessage with EventType 'MealRejected'");
        outbox!.AggregateType.Should().Be("Meal");
        outbox.AggregateId.Should().Be(meal.Id.ToString());

        using var doc = JsonDocument.Parse(outbox.PayloadJson);
        var root = doc.RootElement;
        root.GetProperty("MealId").GetGuid().Should().Be(meal.Id);
        root.GetProperty("RestaurantId").GetGuid().Should().Be(restaurantId);
        root.GetProperty("MealNameAr").GetString().Should().Be(meal.NameAr);
        root.GetProperty("MealNameEn").GetString().Should().Be(meal.NameEn);
        root.GetProperty("RejectionReason").GetString().Should().Be("Inappropriate image");
        root.GetProperty("ReviewedByUserId").GetGuid().Should().Be(adminUserId);
    }
}
