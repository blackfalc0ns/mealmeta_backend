using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.Catalog;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

    [Theory]
    [InlineData("ar", "الكويت")]
    [InlineData("en", "Kuwait")]
    public void Json_includes_header_language_for_ar_en_pairs(string culture, string expectedName)
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var dto = new CallingCodeDto("KW", "الكويت", "Kuwait", "+965", "🇰🇼");
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, options));
            var root = json.RootElement;

            root.GetProperty("name").GetString().Should().Be(expectedName);
            root.TryGetProperty("nameAr", out _).Should().BeFalse();
            root.TryGetProperty("nameEn", out _).Should().BeFalse();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Theory]
    [InlineData("ar", "دايت كير", "وجبات صحية")]
    [InlineData("en", "Diet Care", "Healthy meals")]
    public void Json_includes_localized_fields_for_nested_ar_en_pairs(
        string culture,
        string expectedName,
        string expectedDescription)
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var dto = new NestedCatalogDto(
                new SampleNamedDto("دايت كير", "Diet Care", "وجبات صحية", "Healthy meals"));
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, options));
            var item = json.RootElement.GetProperty("restaurant");

            item.GetProperty("name").GetString().Should().Be(expectedName);
            item.GetProperty("description").GetString().Should().Be(expectedDescription);
            item.TryGetProperty("nameAr", out _).Should().BeFalse();
            item.TryGetProperty("nameEn", out _).Should().BeFalse();
            item.TryGetProperty("descriptionAr", out _).Should().BeFalse();
            item.TryGetProperty("descriptionEn", out _).Should().BeFalse();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Theory]
    [InlineData("ar", "بانتظار التأكيد")]
    [InlineData("en", "Awaiting confirm")]
    public void Json_localized_text_value_follows_ui_culture(string culture, string expected)
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var dto = new LocalizedTextDto("بانتظار التأكيد", "Awaiting confirm");
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, options));

            json.RootElement.ValueKind.Should().Be(JsonValueKind.String);
            json.RootElement.GetString().Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Theory]
    [InlineData("ar", "دايت كير")]
    [InlineData("en", "Diet Care")]
    public void Json_replaces_existing_name_when_ar_en_pair_is_present(string culture, string expectedName)
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var dto = new DualNameDto("diet-care", "دايت كير", "Diet Care");
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, options));
            var root = json.RootElement;

            root.GetProperty("name").GetString().Should().Be(expectedName);
            root.TryGetProperty("nameAr", out _).Should().BeFalse();
            root.TryGetProperty("nameEn", out _).Should().BeFalse();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Theory]
    [InlineData("ar", "دجاج مشوي")]
    [InlineData("en", "Grilled chicken")]
    public void Json_collapses_untyped_database_snapshot_payloads(string culture, string expectedName)
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var snapshot = new Dictionary<string, object?>
            {
                ["NameAr"] = "دجاج مشوي",
                ["NameEn"] = "Grilled chicken",
                ["Slot"] = "lunch",
            };
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(snapshot, options));
            var node = System.Text.Json.Nodes.JsonNode.Parse(json.RootElement.GetRawText())!;
            var collapsed = BilingualJsonCollapser.Collapse(node)!.AsObject();

            collapsed["Name"]!.GetValue<string>().Should().Be(expectedName);
            collapsed.ContainsKey("NameAr").Should().BeFalse();
            collapsed.ContainsKey("NameEn").Should().BeFalse();
            collapsed["Slot"]!.GetValue<string>().Should().Be("lunch");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void Json_keeps_ar_en_source_fields_when_bilingual_scope_is_enabled()
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar");
            using var bilingual = LocalizedJsonScope.Begin(includeSourceFields: true);
            var dto = new CallingCodeDto("KW", "الكويت", "Kuwait", "+965", "🇰🇼");
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, options));
            var root = json.RootElement;

            root.GetProperty("nameAr").GetString().Should().Be("الكويت");
            root.GetProperty("nameEn").GetString().Should().Be("Kuwait");
            root.TryGetProperty("name", out _).Should().BeFalse();

            using var textJson = JsonDocument.Parse(JsonSerializer.Serialize(
                new LocalizedTextDto("مرحبا", "Hello"), options));
            textJson.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
            textJson.RootElement.GetProperty("ar").GetString().Should().Be("مرحبا");
            textJson.RootElement.GetProperty("en").GetString().Should().Be("Hello");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Theory]
    [InlineData("ar", "أدخل رمز التحقق المكون من 6 أرقام.")]
    [InlineData("en", "Enter the 6-digit verification code.")]
    public async Task Http_accept_language_localizes_validation_json(string culture, string expected)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/customers/verify-phone")
        {
            Content = JsonContent.Create(new
            {
                phone = "90000000",
                countryCode = "KW",
                code = "",
            }),
        };
        request.Headers.TryAddWithoutValidation("Accept-Language", culture);

        var response = await client.SendAsync(request);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);

        json.RootElement
            .GetProperty("errors")
            .GetProperty("Code")
            .EnumerateArray()
            .Select(x => x.GetString())
            .Should()
            .Contain(expected);
    }

    private sealed record SampleNamedDto(string NameAr, string NameEn, string? DescriptionAr, string? DescriptionEn);

    private sealed record NestedCatalogDto(SampleNamedDto Restaurant);

    private sealed record DualNameDto(string Name, string NameAr, string NameEn);
}
