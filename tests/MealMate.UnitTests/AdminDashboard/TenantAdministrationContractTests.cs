using FluentAssertions;
using FluentValidation.TestHelper;
using MealMate.Application.Features.AdminDashboard.Tenants;
using MealMate.Domain.Enums;

namespace MealMate.UnitTests.AdminDashboard;

public sealed class TenantAdministrationContractTests
{
    private readonly CreateTenantCommandValidator _createValidator = new();
    private readonly SetTenantStatusCommandValidator _statusValidator = new();

    [Fact]
    public void Create_tenant_accepts_a_valid_international_scope()
    {
        var command = ValidCommand();

        var result = _createValidator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("https://sa.mealmate.app")]
    [InlineData("sa.mealmate.app/path")]
    [InlineData("not a domain")]
    public void Create_tenant_rejects_invalid_domain(string domain)
    {
        var command = ValidCommand() with { Domain = domain };

        var result = _createValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Domain);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("")]
    public void Create_tenant_rejects_unsupported_default_language(string language)
    {
        var command = ValidCommand() with { DefaultLanguage = language };

        var result = _createValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.DefaultLanguage);
    }

    [Fact]
    public void Tenant_access_scope_uses_new_enum_value_without_renumbering_existing_values()
    {
        ((int)AccessScopeType.Global).Should().Be(1);
        ((int)AccessScopeType.Country).Should().Be(2);
        ((int)AccessScopeType.Region).Should().Be(3);
        ((int)AccessScopeType.Restaurant).Should().Be(4);
        ((int)AccessScopeType.Entity).Should().Be(5);
        ((int)AccessScopeType.Tenant).Should().Be(6);
    }

    [Fact]
    public void Status_change_requires_a_reason()
    {
        var command = new SetTenantStatusCommand(Guid.NewGuid(), TenantStatus.Active, "");

        var result = _statusValidator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    private static CreateTenantCommand ValidCommand() =>
        new(
            "TEN-SA",
            "ميل ميت السعودية",
            "MealMate Saudi Arabia",
            "SA",
            "السعودية",
            "Saudi Arabia",
            "SAR",
            "Asia/Riyadh",
            "ar",
            "sa.mealmate.app",
            "إنشاء نطاق التشغيل السعودي");
}
