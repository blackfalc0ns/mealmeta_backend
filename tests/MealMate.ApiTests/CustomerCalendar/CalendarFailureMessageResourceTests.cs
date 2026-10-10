using System.Globalization;
using System.Resources;
using MealMate.Api.Resources;
using Xunit;

namespace MealMate.ApiTests.CustomerCalendar;

public sealed class CalendarFailureMessageResourceTests
{
    [Theory]
    [InlineData("calendar.restaurant_coverage_unavailable")]
    [InlineData("calendar.box_not_ready")]
    [InlineData("calendar.no_safe_box")]
    [InlineData("RESTAURANT_CAPACITY_FULL")]
    public void Calendar_failure_has_distinct_Arabic_and_English_resource(string code)
    {
        var resources = new ResourceManager("MealMate.Api.Resources.Resources.Messages", typeof(Messages).Assembly);
        var en = resources.GetString(code, CultureInfo.GetCultureInfo("en"));
        var ar = resources.GetString(code, CultureInfo.GetCultureInfo("ar"));
        Assert.False(string.IsNullOrWhiteSpace(en));
        Assert.False(string.IsNullOrWhiteSpace(ar));
        Assert.NotEqual(en, ar);
    }
}
