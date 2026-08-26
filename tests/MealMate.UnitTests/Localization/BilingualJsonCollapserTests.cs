using System.Globalization;
using System.Text.Json.Nodes;
using FluentAssertions;
using MealMate.Application.Common.Localization;

namespace MealMate.UnitTests.Localization;

public sealed class BilingualJsonCollapserTests
{
    [Theory]
    [InlineData("ar", "دجاج مشوي", "NameAr")]
    [InlineData("en", "Grilled chicken", "NameEn")]
    public void Collapse_keeps_preferred_language_key_and_unsuffixed_name(
        string culture,
        string expectedName,
        string expectedKey)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var node = JsonNode.Parse("""
                {
                  "mealId": "11111111-1111-1111-1111-111111111111",
                  "NameAr": "دجاج مشوي",
                  "NameEn": "Grilled chicken",
                  "DescriptionAr": "صدر دجاج",
                  "DescriptionEn": "Chicken breast",
                  "ingredients": [
                    { "NameAr": "دجاج", "NameEn": "Chicken", "isLocal": true }
                  ]
                }
                """)!;

            var collapsed = BilingualJsonCollapser.Collapse(node)!.AsObject();

            collapsed[expectedKey]!.GetValue<string>().Should().Be(expectedName);
            collapsed["Name"]!.GetValue<string>().Should().Be(expectedName);
            collapsed.ContainsKey(culture == "ar" ? "NameEn" : "NameAr").Should().BeFalse();
            collapsed["Description"]!.GetValue<string>().Should().Be(culture == "ar" ? "صدر دجاج" : "Chicken breast");

            var ingredient = collapsed["ingredients"]!.AsArray()[0]!.AsObject();
            ingredient["Name"]!.GetValue<string>().Should().Be(culture == "ar" ? "دجاج" : "Chicken");
            ingredient[expectedKey]!.GetValue<string>().Should().Be(culture == "ar" ? "دجاج" : "Chicken");
            ingredient.ContainsKey(culture == "ar" ? "NameEn" : "NameAr").Should().BeFalse();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Theory]
    [InlineData("ar", "دايت كير")]
    [InlineData("en", "Diet Care")]
    public void Collapse_hides_other_language_even_when_name_already_exists(string culture, string expectedName)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var node = JsonNode.Parse("""
                {
                  "name": "diet-care",
                  "nameAr": "دايت كير",
                  "nameEn": "Diet Care",
                  "tradeNameAr": "مطعم النخيل",
                  "tradeNameEn": "Al Nakheel"
                }
                """)!;

            var collapsed = BilingualJsonCollapser.Collapse(node)!.AsObject();

            collapsed["name"]!.GetValue<string>().Should().Be(expectedName);
            collapsed[culture == "ar" ? "nameAr" : "nameEn"]!.GetValue<string>().Should().Be(expectedName);
            collapsed.ContainsKey(culture == "ar" ? "nameEn" : "nameAr").Should().BeFalse();
            collapsed["tradeName"]!.GetValue<string>().Should().Be(culture == "ar" ? "مطعم النخيل" : "Al Nakheel");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void Collapse_turns_ar_en_objects_into_a_single_string()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar");
            var node = JsonNode.Parse("""
                {
                  "label": { "ar": "بانتظار التأكيد", "en": "Awaiting confirm" }
                }
                """)!;

            var collapsed = BilingualJsonCollapser.Collapse(node)!.AsObject();
            collapsed["label"]!.GetValue<string>().Should().Be("بانتظار التأكيد");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void Collapse_keeps_source_fields_in_bilingual_scope()
    {
        using var scope = LocalizedJsonScope.Begin(includeSourceFields: true);
        var node = JsonNode.Parse("""{ "nameAr": "دجاج", "nameEn": "Chicken" }""")!;

        var collapsed = BilingualJsonCollapser.Collapse(node)!.AsObject();

        collapsed["nameAr"]!.GetValue<string>().Should().Be("دجاج");
        collapsed["nameEn"]!.GetValue<string>().Should().Be("Chicken");
        collapsed.ContainsKey("name").Should().BeFalse();
    }
}
