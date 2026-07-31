using System.Globalization;
using FluentAssertions;
using MealMate.Application.Common.Localization;

namespace MealMate.UnitTests.Localization;

public sealed class EmailLanguageTests
{
    [Theory]
    [InlineData("ar", "ar")]
    [InlineData("AR", "ar")]
    [InlineData("ar-SA", "ar")]
    [InlineData("en", "en")]
    [InlineData("en-US", "en")]
    public void Explicit_supported_language_wins(string language, string expected)
        => EmailLanguage.Resolve(language).Should().Be(expected);

    [Theory]
    [InlineData("ar-SA", "ar")]
    [InlineData("en-US", "en")]
    public void Missing_language_uses_current_ui_culture(string cultureName, string expected)
    {
        var originalCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);

            EmailLanguage.Resolve(null).Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("fr")]
    [InlineData("not-a-culture")]
    public void Unsupported_or_empty_language_uses_default(string language)
        => EmailLanguage.Resolve(language)
            .Should().Be(SupportedCultures.Default.TwoLetterISOLanguageName);
}
