using FluentAssertions;
using MealMate.Application.Features.AdminDashboard.Email;

namespace MealMate.UnitTests.AdminDashboard;

public sealed class EmailTemplateCatalogTests
{
    [Fact]
    public void Catalog_contains_all_supported_templates()
        => EmailTemplateCatalog.Keys.Should().HaveCount(7).And.OnlyHaveUniqueItems();

    [Theory]
    [InlineData("ar", "dir=\"rtl\"")]
    [InlineData("en", "dir=\"ltr\"")]
    public void Every_template_renders_complete_safe_html(string language, string direction)
    {
        foreach (var key in EmailTemplateCatalog.Keys)
        {
            var result = EmailTemplateCatalog.Render(
                key, language, EmailTemplateCatalog.SampleVariables());

            result.IsSuccess.Should().BeTrue($"{key} should render in {language}");
            result.Value.Html.Should().Contain(direction);
            result.Value.Html.ToLowerInvariant().Should().NotContain("<script");
            result.Value.Html.Should().NotContain("{{");
        }
    }

    [Fact]
    public void Variables_are_html_encoded()
    {
        var variables = EmailTemplateCatalog.SampleVariables().ToDictionary(x => x.Key, x => x.Value);
        variables["recipient_name"] = "<script>alert('x')</script>";

        var result = EmailTemplateCatalog.Render(
            EmailTemplateCatalog.VerificationOtp, "en", variables);

        result.IsSuccess.Should().BeTrue();
        result.Value.Html.Should().NotContain("<script>");
        result.Value.Html.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void Missing_required_variable_fails()
    {
        var result = EmailTemplateCatalog.Render(
            EmailTemplateCatalog.VerificationOtp,
            "ar",
            new Dictionary<string, string>());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("email.template.variable_missing");
    }

    [Theory]
    [InlineData("unknown", "ar")]
    [InlineData("verification_otp", "fr")]
    public void Invalid_key_or_language_fails(string key, string language)
        => EmailTemplateCatalog.Render(key, language, EmailTemplateCatalog.SampleVariables())
            .IsFailure.Should().BeTrue();
}
