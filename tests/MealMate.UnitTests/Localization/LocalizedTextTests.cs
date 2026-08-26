using System.Globalization;
using FluentAssertions;
using MealMate.Application.Common.Localization;

namespace MealMate.UnitTests.Localization;

public sealed class LocalizedTextTests
{
    [Theory]
    [InlineData("ar", "دجاج", "Chicken", "دجاج")]
    [InlineData("ar-KW", "دجاج", "Chicken", "دجاج")]
    [InlineData("en", "دجاج", "Chicken", "Chicken")]
    [InlineData("en-US", "دجاج", "Chicken", "Chicken")]
    public void Pick_uses_current_ui_culture(string culture, string ar, string en, string expected)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            LocalizedText.Pick(ar, en).Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Theory]
    [InlineData("ar", "", "Chicken", "Chicken")]
    [InlineData("en", "دجاج", "", "دجاج")]
    public void Pick_falls_back_when_preferred_is_blank(string culture, string ar, string en, string expected)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            LocalizedText.Pick(ar, en).Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
