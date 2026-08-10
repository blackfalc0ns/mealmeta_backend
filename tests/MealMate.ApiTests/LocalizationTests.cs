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
    [InlineData("en", "Please sign in to continue.")]
    [InlineData("ar", "يرجى تسجيل الدخول للمتابعة.")]
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
    [InlineData("en", MessageCodes.Auth.DuplicateEmail, "This email address is already registered.")]
    [InlineData("ar", MessageCodes.Auth.DuplicateEmail, "هذا البريد الإلكتروني مسجل مسبقاً.")]
    [InlineData("en", MessageCodes.Auth.DuplicatePhone, "This phone number is already registered.")]
    [InlineData("ar", MessageCodes.Auth.DuplicatePhone, "رقم الهاتف هذا مسجل مسبقاً.")]
    [InlineData("en", MessageCodes.Auth.InvalidCredentials, "The phone number or password is incorrect.")]
    [InlineData("ar", MessageCodes.Auth.InvalidCredentials, "رقم الهاتف أو كلمة المرور غير صحيحة.")]
    [InlineData("en", MessageCodes.Auth.UserNotFound, "We could not find an account with these details.")]
    [InlineData("ar", MessageCodes.Auth.UserNotFound, "لم نتمكن من العثور على حساب بهذه البيانات.")]
    [InlineData("en", MessageCodes.Auth.OtpInvalid, "The verification code is incorrect or has expired.")]
    [InlineData("ar", MessageCodes.Auth.OtpInvalid, "رمز التحقق غير صحيح أو انتهت صلاحيته.")]
    [InlineData("en", MessageCodes.Auth.OtpExpired, "The verification code has expired. Please request a new code.")]
    [InlineData("ar", MessageCodes.Auth.OtpExpired, "انتهت صلاحية رمز التحقق. يرجى طلب رمز جديد.")]
    [InlineData("en", MessageCodes.Auth.OtpSendLimitExceeded, "You have requested too many codes. Please try again later.")]
    [InlineData("ar", MessageCodes.Auth.OtpSendLimitExceeded, "تم طلب رموز كثيرة. يرجى المحاولة لاحقاً.")]
    [InlineData("en", MessageCodes.Auth.InvalidState, "This action is not available for the current account status.")]
    [InlineData("en", MessageCodes.Auth.PasswordTooWeak, "Password must be at least 8 characters and include letters and numbers.")]
    [InlineData("ar", MessageCodes.Auth.PasswordTooWeak, "يجب أن تكون كلمة المرور 8 أحرف على الأقل وتحتوي على حروف وأرقام.")]
    [InlineData("ar", MessageCodes.Auth.InvalidState, "هذا الإجراء غير متاح لحالة الحساب الحالية.")]
    [InlineData("en", MessageCodes.Auth.TotpRequired, "Two-factor verification is required to continue.")]
    [InlineData("ar", MessageCodes.Auth.TotpRequired, "التحقق الثنائي مطلوب للمتابعة.")]
    [InlineData("en", MessageCodes.Validation.OtpCodeInvalid, "Enter the 6-digit verification code.")]
    [InlineData("ar", MessageCodes.Validation.OtpCodeInvalid, "أدخل رمز التحقق المكون من 6 أرقام.")]
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
