using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using MealMate.Api.Localization;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Domain.Enums;

namespace MealMate.ApiTests.Localization;

public sealed class BilingualJsonModifierTests
{
    [Fact]
    public void Serialize_FamilyMemberSummary_KeepsEnumRoleWhenRoleArRoleEnExist()
    {
        var dto = new FamilyMemberSummaryDto(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "أحمد",
            FamilyMemberRole.Manager,
            true,
            "عضو رئيسي",
            "Primary member",
            FamilyMemberSetupStatus.Ready,
            true,
            null,
            "كيتو",
            "Keto",
            null,
            "وجبتان",
            "Two meals",
            RestaurantTier.Platinum,
            "بلاتينيوم",
            "Platinum",
            12,
            "12 يوم",
            "12 days",
            false,
            false,
            true,
            false,
            false,
            1,
            0,
            1,
            0,
            0,
            10m);

        var options = CreateApiJsonOptions();

        var act = () => JsonSerializer.Serialize(dto, options);

        act.Should().NotThrow();
        using var doc = JsonDocument.Parse(act());
        doc.RootElement.GetProperty("role").GetString().Should().Be("Manager");
    }

    [Fact]
    public void Serialize_StringNameWithNameArNameEn_StillEmitsLocalizedName()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var dto = new NamedLabels("ignored", "عربي", "English");
            var json = JsonSerializer.Serialize(dto, CreateApiJsonOptions());
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("name").GetString().Should().Be("English");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private sealed record NamedLabels(string Name, string NameAr, string NameEn);

    private static JsonSerializerOptions CreateApiJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        MealMateJsonSerializer.Configure(options);
        return options;
    }
}
