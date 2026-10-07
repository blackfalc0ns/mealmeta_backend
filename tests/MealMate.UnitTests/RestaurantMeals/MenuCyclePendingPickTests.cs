using MealMate.Application.Features.MenuCycles;
using Xunit;

namespace MealMate.UnitTests.RestaurantMeals;

public sealed class MenuCyclePendingPickTests
{
    [Fact]
    public void PickDayId_UsesActiveUntilLoopEnds_ThenPending()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var active = new[] { a, b, c };
        var pending = new[] { c, a, b };

        Assert.Equal(a, MenuCyclePendingSequence.PickDayIdForWorkingIndex(active, pending, 0));
        Assert.Equal(c, MenuCyclePendingSequence.PickDayIdForWorkingIndex(active, pending, 2));
        Assert.Equal(c, MenuCyclePendingSequence.PickDayIdForWorkingIndex(active, pending, 3));
        Assert.Equal(a, MenuCyclePendingSequence.PickDayIdForWorkingIndex(active, pending, 4));
    }
}
