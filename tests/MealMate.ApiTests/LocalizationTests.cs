using System.Globalization;
using FluentAssertions;
using MealMate.Application.Common.Localization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.ApiTests;

public class LocalizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LocalizationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Theory]
    [InlineData("en", "Authentication is required.")]
    [InlineData("ar", "مطلوب تسجيل الدخول.")]
    public void GetMessage_UsesRequestCulture(string culture, string expected)
    {
        using var scope = _factory.Services.CreateScope();
        var cultureInfo = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentCulture = cultureInfo;
        CultureInfo.CurrentUICulture = cultureInfo;

        var provider = scope.ServiceProvider.GetRequiredService<ILocalizedMessageProvider>();
        var message = provider.GetMessage(MessageCodes.Common.Unauthorized);

        message.Should().Be(expected);
    }

    [Fact]
    public void GetMessage_DomainMoneyCode_ReturnsArabic()
    {
        using var scope = _factory.Services.CreateScope();
        var cultureInfo = CultureInfo.GetCultureInfo("ar");
        CultureInfo.CurrentCulture = cultureInfo;
        CultureInfo.CurrentUICulture = cultureInfo;

        var provider = scope.ServiceProvider.GetRequiredService<ILocalizedMessageProvider>();
        var message = provider.GetMessage(MessageCodes.Domain.MoneyCurrencyMismatch);

        message.Should().Be("لا يمكن إجراء العملية على عملات مختلفة.");
    }
}
