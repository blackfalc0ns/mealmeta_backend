using System.Globalization;
using FluentAssertions;
using MealMate.Application.Common.Localization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.ApiTests;

public class LocalizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LocalizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Seed:Identity", "false");
        });
    }

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

    [Theory]
    [InlineData("en", MessageCodes.Auth.DuplicateEmail, "Email is already registered.")]
    [InlineData("ar", MessageCodes.Auth.DuplicateEmail, "البريد الإلكتروني مسجل مسبقاً.")]
    [InlineData("en", MessageCodes.Auth.UserNotFound, "User not found.")]
    [InlineData("ar", MessageCodes.Auth.UserNotFound, "المستخدم غير موجود.")]
    [InlineData("en", MessageCodes.Auth.OtpInvalid, "Invalid or expired verification code.")]
    [InlineData("ar", MessageCodes.Auth.OtpInvalid, "رمز التحقق غير صحيح أو منتهي الصلاحية.")]
    [InlineData("en", MessageCodes.Auth.InvalidState, "Invalid state for this operation.")]
    [InlineData("en", MessageCodes.Auth.PasswordTooWeak, "Password must be at least 8 characters and contain at least one letter and one number.")]
    [InlineData("ar", MessageCodes.Auth.InvalidState, "حالة غير صالحة لهذه العملية.")]
    [InlineData("en", MessageCodes.Auth.DocumentsIncomplete, "Required documents are incomplete.")]
    [InlineData("ar", MessageCodes.Auth.DocumentsIncomplete, "المستندات المطلوبة غير مكتملة.")]
    [InlineData("en", MessageCodes.Auth.DuplicateCommercialRegistration, "Commercial registration number is already in use.")]
    [InlineData("ar", MessageCodes.Auth.DuplicateCommercialRegistration, "رقم السجل التجاري مستخدم مسبقاً.")]
    public void GetMessage_AuthCodes_ResolvesInBothCultures(string culture, string code, string expected)
    {
        using var scope = _factory.Services.CreateScope();
        var cultureInfo = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentCulture = cultureInfo;
        CultureInfo.CurrentUICulture = cultureInfo;

        var provider = scope.ServiceProvider.GetRequiredService<ILocalizedMessageProvider>();
        var message = provider.GetMessage(code);

        message.Should().Be(expected);
    }
}
