using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.MenuCycles;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantMeals;

public sealed class MenuPlanTests
{
    [Fact]
    public async Task IndependentCycles_AdvanceSeparately_ForTwoPrograms()
    {
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var weight = Guid.NewGuid();
        var fitness = Guid.NewGuid();
        var start = new DateOnly(2026, 8, 11);

        db.MenuPlans.Add(CreatePlan(restaurantId, weight, 11, start, "Weight"));
        db.MenuPlans.Add(CreatePlan(restaurantId, fitness, 9, start, "Fitness"));
        await db.SaveChangesAsync();

        var handlers = new MenuCycleHandlers(db);
        var weightResolved = await handlers.Handle(new ResolveMenuPlanQuery(restaurantId, weight, start.AddDays(11)), CancellationToken.None);
        var fitnessResolved = await handlers.Handle(new ResolveMenuPlanQuery(restaurantId, fitness, start.AddDays(9)), CancellationToken.None);

        Assert.True(weightResolved.IsSuccess);
        Assert.True(fitnessResolved.IsSuccess);
        Assert.Equal(1, weightResolved.Value.DayNumber);
        Assert.Equal(1, fitnessResolved.Value.DayNumber);
        Assert.NotEqual(weightResolved.Value.PlanId, fitnessResolved.Value.PlanId);
    }

    [Fact]
    public async Task Weekday_PrimaryAlternatives_ResolveForServiceDate()
    {
        await using var db = CreateDb();
        var program = new Program { Code = "W", NameAr = "ب", NameEn = "P", IsActive = true, CreatedAtUtc = DateTime.UtcNow };
        var programId = program.Id;
        var restaurantId = Guid.NewGuid();
        var start = new DateOnly(2026, 8, 11); // Tuesday
        var plan = new MenuPlan
        {
            RestaurantId = restaurantId, NameAr = "أسبوعي", NameEn = "Weekly",
            Source = MenuPlanSource.RestaurantCreated, Mode = ScheduleMode.Weekday, CreatedAtUtc = DateTime.UtcNow,
        };
        var version = new MenuPlanVersion
        {
            MenuPlan = plan, VersionNumber = 1, EffectiveFrom = start,
            Status = VerificationStatus.Approved, CreatedAtUtc = DateTime.UtcNow,
        };
        var menu = new Menu { RestaurantId = restaurantId, ProgramId = programId, NameAr = "م", NameEn = "M", CreatedAtUtc = DateTime.UtcNow };
        var primary = new Meal { RestaurantId = restaurantId, ProgramId = programId, Menu = menu, Code = "P", NameAr = "أساسية", NameEn = "Primary", Slot = "lunch", ApprovalStatus = VerificationStatus.Approved, CreatedAtUtc = DateTime.UtcNow };
        var alt = new Meal { RestaurantId = restaurantId, ProgramId = programId, Menu = menu, Code = "A", NameAr = "بديل", NameEn = "Alt", Slot = "lunch", ApprovalStatus = VerificationStatus.Approved, CreatedAtUtc = DateTime.UtcNow };
        version.WeekdayAssignments.Add(new WeekdayMealAssignment { DayOfWeek = DayOfWeek.Tuesday, ProgramId = programId, Meal = primary, Slot = "lunch", IsPrimary = true, CreatedAtUtc = DateTime.UtcNow });
        version.WeekdayAssignments.Add(new WeekdayMealAssignment { DayOfWeek = DayOfWeek.Tuesday, ProgramId = programId, Meal = alt, Slot = "lunch", IsPrimary = false, SortOrder = 1, CreatedAtUtc = DateTime.UtcNow });
        plan.Versions.Add(version);
        plan.Programs.Add(new MenuPlanProgram { ProgramId = programId, EffectiveFrom = start, IsPrimaryOwner = true, CreatedAtUtc = DateTime.UtcNow });
        db.AddRange(program, plan, menu, primary, alt);
        await db.SaveChangesAsync();

        var result = await new MenuCycleHandlers(db).Handle(new ResolveMenuPlanQuery(restaurantId, programId, start), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(ScheduleMode.Weekday, result.Value.Mode);
        Assert.Null(result.Value.MenuDayId);
        Assert.Equal(2, result.Value.Meals.Count);
        Assert.Contains(result.Value.Meals, x => x.IsPrimary && x.MealId == primary.Id);
    }

    private static MenuPlan CreatePlan(Guid restaurantId, Guid programId, int dayCount, DateOnly start, string name)
    {
        var plan = new MenuPlan
        {
            RestaurantId = restaurantId, NameAr = name, NameEn = name,
            Source = MenuPlanSource.RestaurantCreated, Mode = ScheduleMode.Cycle, CreatedAtUtc = DateTime.UtcNow,
        };
        var version = new MenuPlanVersion
        {
            MenuPlan = plan, VersionNumber = 1, EffectiveFrom = start,
            Status = VerificationStatus.Approved, CreatedAtUtc = DateTime.UtcNow,
        };
        for (var i = 1; i <= dayCount; i++)
            version.Days.Add(new MenuDay { MenuPlanVersion = version, DayNumber = i, NameAr = $"يوم {i}", NameEn = $"Day {i}", CreatedAtUtc = DateTime.UtcNow });
        plan.Versions.Add(version);
        plan.Programs.Add(new MenuPlanProgram { ProgramId = programId, EffectiveFrom = start, IsPrimaryOwner = true, CreatedAtUtc = DateTime.UtcNow });
        return plan;
    }

    private static Persistence.AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Persistence.AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new Persistence.AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }
}
