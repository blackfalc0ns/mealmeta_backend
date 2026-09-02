using MealMate.Application.Features.MenuCycles;
using Xunit;

namespace MealMate.UnitTests.RestaurantMeals;

public sealed class MenuCyclePendingSequenceTests
{
    [Fact]
    public void Insert_AtFirst_ShiftsOthers()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var m = Guid.NewGuid();
        var result = MenuCyclePendingSequence.Insert(new[] { a, b }, m, 1);
        Assert.Equal(new[] { m, a, b }, result);
    }

    [Fact]
    public void Swap_ExchangesPositions()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var result = MenuCyclePendingSequence.Swap(new[] { a, b, c }, c, 1);
        Assert.Equal(new[] { c, b, a }, result);
    }

    [Fact]
    public void ShouldActivate_OnlyAfterLastSlotOfLoop()
    {
        Assert.False(MenuCyclePendingSequence.ShouldActivate(previousWorkingIndex: -1, activeCount: 12));
        Assert.False(MenuCyclePendingSequence.ShouldActivate(previousWorkingIndex: 10, activeCount: 12));
        Assert.True(MenuCyclePendingSequence.ShouldActivate(previousWorkingIndex: 11, activeCount: 12));
        Assert.True(MenuCyclePendingSequence.ShouldActivate(previousWorkingIndex: 23, activeCount: 12));
    }

    [Fact]
    public void ResolveOrder_UsesPendingAfterActivationBoundary()
    {
        var active = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var pending = new[] { active[2], active[0], active[1] }.ToList();
        var before = MenuCyclePendingSequence.ResolveOrderForWorkingIndex(active, pending, 2, 3);
        Assert.Equal(active, before);
        var after = MenuCyclePendingSequence.ResolveOrderForWorkingIndex(active, pending, 3, 3);
        Assert.Equal(pending, after);
    }
}
