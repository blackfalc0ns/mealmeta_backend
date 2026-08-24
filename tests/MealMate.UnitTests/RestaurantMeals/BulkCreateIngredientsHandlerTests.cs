using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.RestaurantMeals;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.RestaurantMeals;

public sealed class BulkCreateIngredientsHandlerTests
{
    [Fact]
    public async Task BulkCreate_skips_existing_codes_and_creates_new()
    {
        var restaurantId = Guid.NewGuid();
        var db = CreateDb();
        db.Ingredients.Add(new Ingredient
        {
            RestaurantId = restaurantId,
            Code = "ING-EXIST",
            NameAr = "قديم",
            NameEn = "Old",
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var handler = new IngredientHandlers(db);
        var result = await handler.Handle(
            new BulkCreateIngredientsCommand(
                restaurantId,
                [
                    new BulkIngredientItemDto("ING-EXIST", "تحديث", "Updated"),
                    new BulkIngredientItemDto("ING-NEW", "صدر دجاج", "Chicken Breast", Category: "poultry"),
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Created);
        Assert.Equal(1, result.Value.SkippedDuplicate);
        Assert.Equal(0, result.Value.SkippedInvalid);

        var existing = await db.Ingredients.SingleAsync(x => x.Code == "ING-EXIST");
        Assert.Equal("قديم", existing.NameAr);

        var created = await db.Ingredients.SingleAsync(x => x.Code == "ING-NEW");
        Assert.Equal("Chicken Breast", created.NameEn);
        Assert.Equal("poultry", created.Category);
    }

    [Fact]
    public async Task BulkCreate_skips_invalid_rows_without_failing_batch()
    {
        var restaurantId = Guid.NewGuid();
        var db = CreateDb();
        var handler = new IngredientHandlers(db);

        var result = await handler.Handle(
            new BulkCreateIngredientsCommand(
                restaurantId,
                [
                    new BulkIngredientItemDto("ING-1", "", "Missing Arabic"),
                    new BulkIngredientItemDto("ING-2", "أرز", "Rice", Category: "grains"),
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Created);
        Assert.Equal(1, result.Value.SkippedInvalid);
        Assert.Contains(result.Value.Issues, issue => issue.Reason == "missing_name");
        Assert.Equal(1, await db.Ingredients.CountAsync(x => x.RestaurantId == restaurantId));
    }

    private static AppDbContext CreateDb() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
}
