using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using MealMate.Api.Localization;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Domain.Enums;

namespace MealMate.ApiTests.CustomerCalendar;

/// <summary>
/// Serialization contract for the calendar DTOs under the request language:
/// Accept-Language ar collapses fooAr/fooEn into foo (Arabic value), en collapses to
/// the English value, and X-Bilingual: true keeps both source fields. This mirrors
/// what BilingualJsonModifier does on the real response path.
/// </summary>
public sealed class CustomerCalendarLocalizedContractTests
{
    private static CustomerDayRestaurantDto Restaurant() => new(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        "مطبخ دايت كير",
        "Diet Care Kitchen",
        "https://logo",
        RestaurantTier.Basic,
        UsedCount: 1,
        Limit: 8,
        RemainingCount: 7,
        IsBusy: false,
        IsQuotaExhausted: false,
        IsEnabled: true,
        DisabledReason: null,
        SafeMealCount: 4,
        Rating: null,
        RatingCount: 0,
        DeliveryWindowStart: new TimeOnly(8, 0),
        DeliveryWindowEnd: new TimeOnly(16, 0),
        DeliveryTimeText: "8:00 ص - 4:00 م",
        TierNameAr: "أساسي",
        TierNameEn: "Basic",
        TierName: null,
        TierLogoUrl: null);

    [Fact]
    public void ArabicRequest_EmitsNameArAndCollapsedName_SuppressesNameEn()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar");
        try
        {
            var json = JsonSerializer.Serialize(Restaurant(), CreateApiJsonOptions());
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("name").GetString().Should().Be("مطبخ دايت كير");
            doc.RootElement.GetProperty("nameAr").GetString().Should().Be("مطبخ دايت كير");
            doc.RootElement.TryGetProperty("nameEn", out _).Should().BeFalse();
            doc.RootElement.GetProperty("tierName").GetString().Should().Be("أساسي");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void EnglishRequest_EmitsNameEnAndCollapsedName_SuppressesNameAr()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var json = JsonSerializer.Serialize(Restaurant(), CreateApiJsonOptions());
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("name").GetString().Should().Be("Diet Care Kitchen");
            doc.RootElement.GetProperty("nameEn").GetString().Should().Be("Diet Care Kitchen");
            doc.RootElement.TryGetProperty("nameAr", out _).Should().BeFalse();
            doc.RootElement.GetProperty("tierName").GetString().Should().Be("Basic");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void BilingualScope_KeepsBothSourceFields_AndOmitsCollapsedName()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        using var scope = LocalizedJsonScope.Begin(includeSourceFields: true);
        {
            var json = JsonSerializer.Serialize(Restaurant(), CreateApiJsonOptions());
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("nameAr").GetString().Should().Be("مطبخ دايت كير");
            doc.RootElement.GetProperty("nameEn").GetString().Should().Be("Diet Care Kitchen");
            doc.RootElement.TryGetProperty("name", out _).Should().BeFalse();
        }
        CultureInfo.CurrentUICulture = previous;
    }

    private static JsonSerializerOptions CreateApiJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        MealMateJsonSerializer.Configure(options);
        return options;
    }
}
