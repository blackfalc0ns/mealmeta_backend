using System.Globalization;
using System.Resources;
using MealMate.Api.Resources;
using Xunit;

namespace MealMate.ApiTests.CustomerCheckout;

public sealed class FreezeMessageResourceTests
{
    [Theory]
    [InlineData("freeze.preparation_unavailable")]
    [InlineData("freeze.no_service_days")]
    [InlineData("freeze.replacement_pool_unavailable")]
    [InlineData("freeze.replacement_capacity_unavailable")]
    [InlineData("freeze.replacement_restaurants_unavailable")]
    [InlineData("freeze.replacement_menu_changed")]
    [InlineData("freeze.replacement_meals_unavailable")]
    public void Freeze_failure_resolves_distinct_Arabic_and_English_copy(string code)
    {
        var resources = new ResourceManager("MealMate.Api.Resources.Resources.Messages", typeof(Messages).Assembly);
        var en = resources.GetString(code, CultureInfo.GetCultureInfo("en"));
        var ar = resources.GetString(code, CultureInfo.GetCultureInfo("ar"));
        Assert.False(string.IsNullOrWhiteSpace(en));
        Assert.False(string.IsNullOrWhiteSpace(ar));
        Assert.NotEqual(en, ar);
    }
}
