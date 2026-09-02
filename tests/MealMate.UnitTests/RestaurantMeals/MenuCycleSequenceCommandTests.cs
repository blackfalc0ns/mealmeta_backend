using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.MenuCycles;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantMeals;

public sealed class MenuCycleSequenceCommandTests
{
    [Fact]
    public async Task Add_AtFirst_CreatesInactiveDay_AndPending_WithoutChangingActiveCount()
    {
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var plan = CreateApprovedCycle(restaurantId, dayCount: 3);
        db.MenuPlans.Add(plan);
        await db.SaveChangesAsync();

        var result = await new MenuCycleHandlers(db).Handle(
            new AddMenuCycleSequenceDayCommand(restaurantId, plan.Id, "منيو جديد", "New menu", 1),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var version = await db.MenuPlanVersions.Include(x => x.Days).FirstAsync(x => x.MenuPlanId == plan.Id);
        Assert.Equal(3, version.Days.Count(x => !x.IsDeleted && x.IsActiveInCycle));
        Assert.Equal(1, version.Days.Count(x => !x.IsDeleted && !x.IsActiveInCycle));
        var pending = MenuCyclePendingSequence.Parse(version.PendingSequenceJson);
        Assert.Equal(4, pending.Count);
        Assert.Equal(version.Days.Single(x => !x.IsActiveInCycle).Id, pending[0]);
        Assert.True(result.Value.PendingAppliesAfterCurrentLoop);
        Assert.Equal(4, result.Value.PendingSequence!.Count);
        Assert.Equal(3, result.Value.ActiveSequence!.Count);
    }

    [Fact]
    public async Task Swap_ChangesPending_Only()
    {
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var plan = CreateApprovedCycle(restaurantId, dayCount: 3);
        db.MenuPlans.Add(plan);
        await db.SaveChangesAsync();
        var days = plan.Versions.Single().Days.OrderBy(x => x.DayNumber).ToList();
        var third = days[2];

        var result = await new MenuCycleHandlers(db).Handle(
            new SwapMenuCycleSequenceDayCommand(restaurantId, plan.Id, third.Id, 1),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var version = await db.MenuPlanVersions.Include(x => x.Days).FirstAsync(x => x.MenuPlanId == plan.Id);
        var orderedActive = version.Days.Where(x => x.IsActiveInCycle).OrderBy(x => x.DayNumber).Select(x => x.Id).ToList();
        Assert.Equal(days.Select(x => x.Id), orderedActive);
        var pending = MenuCyclePendingSequence.Parse(version.PendingSequenceJson);
        Assert.Equal(third.Id, pending[0]);
        Assert.Equal(days[0].Id, pending[2]);
    }

    [Fact]
    public async Task Add_Rejects_WeekdayPlan()
    {
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var plan = CreateApprovedCycle(restaurantId, dayCount: 1);
        plan.Mode = ScheduleMode.Weekday;
        db.MenuPlans.Add(plan);
        await db.SaveChangesAsync();

        var result = await new MenuCycleHandlers(db).Handle(
            new AddMenuCycleSequenceDayCommand(restaurantId, plan.Id, "أ", "A", 1),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("menu_plan.mode_mismatch", result.Error.Code);
    }

    [Fact]
    public async Task Cancel_ClearsPending_AndDeletesEmptyInactiveDays()
    {
        await using var db = CreateDb();
        var restaurantId = Guid.NewGuid();
        var plan = CreateApprovedCycle(restaurantId, dayCount: 2);
        db.MenuPlans.Add(plan);
        await db.SaveChangesAsync();

        var add = await new MenuCycleHandlers(db).Handle(
            new AddMenuCycleSequenceDayCommand(restaurantId, plan.Id, "جديد", "New", 1),
            CancellationToken.None);
        Assert.True(add.IsSuccess);

        var cancel = await new MenuCycleHandlers(db).Handle(
            new CancelMenuCyclePendingSequenceCommand(restaurantId, plan.Id),
            CancellationToken.None);

        Assert.True(cancel.IsSuccess);
        var version = await db.MenuPlanVersions.Include(x => x.Days).FirstAsync(x => x.MenuPlanId == plan.Id);
        Assert.True(string.IsNullOrWhiteSpace(version.PendingSequenceJson));
        Assert.Equal(0, version.Days.Count(x => !x.IsDeleted && !x.IsActiveInCycle));
        Assert.False(cancel.Value.PendingAppliesAfterCurrentLoop);
    }

    private static MenuPlan CreateApprovedCycle(Guid restaurantId, int dayCount)
    {
        var cycle = new MenuPlan
        {
            RestaurantId = restaurantId,
            NameAr = "الدورة",
            NameEn = "Cycle",
            Source = MenuPlanSource.RestaurantCreated,
            Mode = ScheduleMode.Cycle,
            CreatedAtUtc = DateTime.UtcNow,
        };
        var version = new MenuPlanVersion
        {
            MenuPlan = cycle,
            VersionNumber = 1,
            EffectiveFrom = new DateOnly(2026, 8, 1),
            Status = VerificationStatus.Approved,
            CreatedAtUtc = DateTime.UtcNow,
        };
        for (var index = 1; index <= dayCount; index++)
        {
            version.Days.Add(new MenuDay
            {
                MenuPlanVersion = version,
                DayNumber = index,
                NameAr = $"منيو {index}",
                NameEn = $"Menu {index}",
                IsActiveInCycle = true,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }
        cycle.Versions.Add(version);
        return cycle;
    }

    private static Persistence.AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<Persistence.AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Persistence.AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }
}
