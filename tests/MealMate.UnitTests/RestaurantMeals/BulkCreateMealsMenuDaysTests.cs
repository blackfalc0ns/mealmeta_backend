using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.RestaurantMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantMeals;

public class BulkCreateMealsMenuDaysTests
{
    [Fact]
    public async Task BulkCreateMeals_With_MenuDayIds_Persists_MenuDayMeals()
    {
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();

        var program = new Program
        {
            Code = "PROG-01",
            NameAr = "برنامج تجريبي",
            NameEn = "Test Program",
            IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id,
            Program = program,
            Code = "BUNDLE-01",
            NameAr = "باقة تجريبية",
            NameEn = "Test Bundle",
            MealsPerDay = 1,
            IsActive = true,
        };
        db.Programs.Add(program);
        db.Bundles.Add(bundle);
        db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            RestaurantId = restaurantId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            IsActive = true
        });

        var plan = new MenuPlan
        {
            RestaurantId = restaurantId,
            NameAr = "خطة الدورة",
            NameEn = "Cycle Plan",
            Source = MenuPlanSource.RestaurantCreated,
            Mode = ScheduleMode.Cycle,
        };
        var version = new MenuPlanVersion
        {
            MenuPlan = plan,
            VersionNumber = 1,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = VerificationStatus.Draft,
        };
        var day1 = new MenuDay
        {
            MenuPlanVersion = version,
            DayNumber = 1,
            NameAr = "اليوم 1",
            NameEn = "Day 1",
            CreatedAtUtc = DateTime.UtcNow,
        };
        var day2 = new MenuDay
        {
            MenuPlanVersion = version,
            DayNumber = 2,
            NameAr = "اليوم 2",
            NameEn = "Day 2",
            CreatedAtUtc = DateTime.UtcNow,
        };
        version.Days.Add(day1);
        version.Days.Add(day2);
        plan.Versions.Add(version);
        db.MenuPlans.Add(plan);
        await db.SaveChangesAsync();

        var handler = new MealHandlers(db);
        var bulkItem = new BulkMealItemDto(
            Code: "MEAL-BULK-01",
            NameAr: "وجبة صحية",
            NameEn: "Healthy Meal",
            ProgramId: program.Id,
            Slot: "lunch",
            MenuDayIds: new[] { day1.Id, day2.Id });

        var command = new BulkCreateMealsCommand(
            RestaurantId: restaurantId,
            MenuId: null,
            Items: new List<BulkMealItemDto> { bulkItem });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Created);

        var createdMeal = await db.Meals
            .Include(m => m.MenuDayMeals)
            .FirstOrDefaultAsync(m => m.Code == "MEAL-BULK-01");

        Assert.NotNull(createdMeal);
        Assert.Equal(2, createdMeal.MenuDayMeals.Count);
        Assert.Contains(createdMeal.MenuDayMeals, x => x.MenuDayId == day1.Id);
        Assert.Contains(createdMeal.MenuDayMeals, x => x.MenuDayId == day2.Id);
    }

    [Fact]
    public async Task BulkCreateMeals_Without_MenuDayIds_Succeeds_With_Zero_MenuDayMeals()
    {
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();

        var program = new Program
        {
            Code = "PROG-02",
            NameAr = "برنامج ثاني",
            NameEn = "Second Program",
            IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id,
            Program = program,
            Code = "BUNDLE-02",
            NameAr = "باقة ثانية",
            NameEn = "Second Bundle",
            MealsPerDay = 1,
            IsActive = true,
        };
        db.Programs.Add(program);
        db.Bundles.Add(bundle);
        db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            RestaurantId = restaurantId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var handler = new MealHandlers(db);
        var bulkItem = new BulkMealItemDto(
            Code: "MEAL-BULK-02",
            NameAr: "وجبة عادية",
            NameEn: "Regular Meal",
            ProgramId: program.Id,
            Slot: "lunch",
            MenuDayIds: null);

        var command = new BulkCreateMealsCommand(
            RestaurantId: restaurantId,
            MenuId: null,
            Items: new List<BulkMealItemDto> { bulkItem });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Created);

        var createdMeal = await db.Meals
            .Include(m => m.MenuDayMeals)
            .FirstOrDefaultAsync(m => m.Code == "MEAL-BULK-02");

        Assert.NotNull(createdMeal);
        Assert.Empty(createdMeal.MenuDayMeals);
    }

    private static Persistence.AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Persistence.AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Persistence.AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }
}
