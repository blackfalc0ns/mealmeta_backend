using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.MenuCycles;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantMeals;

public sealed class MenuCycleTests
{
    [Fact]
    public async Task AdminList_BuildsReadinessAndPreviewFromSummaryData()
    {
        await using var db = CreateDb();
        var restaurant = CreateRestaurant();
        var program = new Program
        {
            Code = "LIST",
            NameAr = "برنامج القائمة",
            NameEn = "List program",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        var menu = new Menu
        {
            RestaurantId = restaurant.Id,
            ProgramId = program.Id,
            NameAr = "قائمة",
            NameEn = "Menu",
            CreatedAtUtc = DateTime.UtcNow,
        };
        var meal = new Meal
        {
            RestaurantId = restaurant.Id,
            ProgramId = program.Id,
            Menu = menu,
            MenuId = menu.Id,
            Code = "LIST-1",
            NameAr = "وجبة القائمة",
            NameEn = "List meal",
            Slot = "breakfast",
            ApprovalStatus = VerificationStatus.Approved,
            IsAvailableForNewOrders = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        var cycle = CreateCycle(restaurant.Id, 1, VerificationStatus.Approved, DateOnly.FromDateTime(DateTime.UtcNow));
        var day = cycle.Versions.Single().Days.Single();
        day.Meals.Add(new MenuDayMeal
        {
            MenuDay = day,
            Meal = meal,
            ProgramId = program.Id,
            Slot = "breakfast",
            CreatedAtUtc = DateTime.UtcNow,
        });
        db.AddRange(
            restaurant,
            program,
            menu,
            meal,
            cycle,
            new RestaurantProgramBundleOffer
            {
                RestaurantId = restaurant.Id,
                ProgramId = program.Id,
                BundleId = Guid.NewGuid(),
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
            },
            new ProgramMealSlotRequirement
            {
                ProgramId = program.Id,
                Slot = "breakfast",
                IsRequired = true,
                CreatedAtUtc = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();

        var result = await new MenuCycleHandlers(db).Handle(
            new GetAdminMenuCyclesQuery(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value);
        Assert.Equal(1, item.ReadyDays);
        Assert.Equal(1, item.TotalDays);
        Assert.Equal(100, item.ReadinessPercent);
        Assert.Equal(1, item.TodayMenuNumber);
        Assert.Equal(1, item.NextMenuNumber);
    }

    [Fact]
    public async Task CreateCycle_Creates_Eleven_Ordered_General_Menu_Days()
    {
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var days = Enumerable.Range(1, 11).Select(x => new MenuCycleDayInput(x, $"منيو {x}", $"Menu {x}")).ToList();
        var program = new Program
        {
            Code = "CREATE",
            NameAr = "برنامج الإنشاء",
            NameEn = "Create program",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        var programId = program.Id;
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        var result = await new MenuCycleHandlers(db).Handle(new CreateRestaurantMenuCycleCommand(
            restaurantId, "الدورة", "Cycle", new DateOnly(2026, 8, 11), days, [programId]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsTemplate);
        Assert.Equal([programId], result.Value.ProgramIds);
        Assert.Equal(11, result.Value.CurrentVersion!.Days.Count);
        Assert.Equal(Enumerable.Range(1, 11), result.Value.CurrentVersion.Days.Select(x => x.DayNumber));
        Assert.All(result.Value.CurrentVersion.Days, day =>
        {
            var readiness = Assert.Single(day.Programs);
            Assert.Equal(programId, readiness.ProgramId);
        });
    }

    [Fact]
    public async Task CreateCycle_Requires_Exactly_One_Program()
    {
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var days = Enumerable.Range(1, 2).Select(x => new MenuCycleDayInput(x, $"منيو {x}", $"Menu {x}")).ToList();

        var result = await new MenuCycleHandlers(db).Handle(new CreateRestaurantMenuCycleCommand(
            restaurantId, "الدورة", "Cycle", new DateOnly(2026, 8, 11), days, [Guid.NewGuid(), Guid.NewGuid()]), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("menu_plan.program_required", result.Error.Code);
    }

    [Fact]
    public async Task Preview_Wraps_After_Eleven_Working_Days()
    {
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var cycle = CreateCycle(restaurantId, 11, VerificationStatus.Approved);
        db.MenuPlans.Add(cycle);
        await db.SaveChangesAsync();

        var result = await new MenuCycleHandlers(db).Handle(new PreviewRestaurantMenuCycleQuery(
            restaurantId, cycle.Id, new DateOnly(2026, 8, 11), 14), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value[0].DayNumber);
        Assert.Equal(11, result.Value[10].DayNumber);
        Assert.Equal(1, result.Value[11].DayNumber);
    }

    [Fact]
    public async Task Preview_Does_Not_Consume_Closed_Day()
    {
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var start = new DateOnly(2026, 8, 11);
        var cycle = CreateCycle(restaurantId, 3, VerificationStatus.Approved, start);
        db.MenuPlans.Add(cycle);
        db.RestaurantOperatingDayExceptions.Add(new RestaurantOperatingDayException
        {
            RestaurantId = restaurantId, Date = start.AddDays(1), IsWorkingDay = false,
            Reason = "Closed", CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await new MenuCycleHandlers(db).Handle(new PreviewRestaurantMenuCycleQuery(
            restaurantId, cycle.Id, start, 14), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value[0].DayNumber);
        Assert.False(result.Value[1].IsWorkingDay);
        Assert.Null(result.Value[1].DayNumber);
        Assert.Equal(2, result.Value[2].DayNumber);
    }

    [Fact]
    public async Task LinkMeal_Rejects_MenuDay_From_Another_Restaurant()
    {
        await using var db = CreateDb();
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var cycle = CreateCycle(ownerId, 1, VerificationStatus.Draft);
        var menu = new Menu { RestaurantId = otherId, ProgramId = Guid.NewGuid(), NameAr = "وجبات", NameEn = "Meals", CreatedAtUtc = DateTime.UtcNow };
        var meal = new Meal { RestaurantId = otherId, ProgramId = menu.ProgramId, Menu = menu, MenuId = menu.Id, Code = "M1", NameAr = "وجبة", NameEn = "Meal", Slot = "lunch", CreatedAtUtc = DateTime.UtcNow };
        db.AddRange(cycle, menu, meal);
        await db.SaveChangesAsync();

        var dayId = cycle.Versions.Single().Days.Single().Id;
        var result = await new MenuCycleHandlers(db).Handle(new SetMenuDayMealCommand(ownerId, dayId, meal.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("meal.program_required", result.Error.Code);
    }

    [Fact]
    public void DayValidator_Rejects_Invalid_Day()
    {
        var result = new MenuCycleDayInputValidator().Validate(new MenuCycleDayInput(0, "", ""));
        Assert.False(result.IsValid);
    }

    private static MenuPlan CreateCycle(Guid restaurantId, int dayCount, VerificationStatus status, DateOnly? start = null)
    {
        var cycle = new MenuPlan { RestaurantId = restaurantId, NameAr = "الدورة", NameEn = "Cycle", Source = MenuPlanSource.RestaurantCreated, Mode = ScheduleMode.Cycle, CreatedAtUtc = DateTime.UtcNow };
        var version = new MenuPlanVersion { MenuPlan = cycle, VersionNumber = 1, EffectiveFrom = start ?? new DateOnly(2026, 8, 11), Status = status, CreatedAtUtc = DateTime.UtcNow };
        for (var index = 1; index <= dayCount; index++) version.Days.Add(new MenuDay { MenuPlanVersion = version, DayNumber = index, NameAr = $"منيو {index}", NameEn = $"Menu {index}", CreatedAtUtc = DateTime.UtcNow });
        cycle.Versions.Add(version);
        return cycle;
    }

    private static RestaurantProfile CreateRestaurant() => new()
    {
        UserId = Guid.NewGuid(),
        LegalNameAr = "شركة اختبار",
        LegalNameEn = "Test Company",
        TradeNameAr = "مطعم اختبار",
        TradeNameEn = "Test Restaurant",
        VerificationStatus = VerificationStatus.Approved,
        IsOperationallyActive = true,
        CreatedAtUtc = DateTime.UtcNow,
    };

    private static Persistence.AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Persistence.AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new Persistence.AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }
}
