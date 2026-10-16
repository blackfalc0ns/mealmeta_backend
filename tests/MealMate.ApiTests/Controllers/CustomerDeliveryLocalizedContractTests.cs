using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using MealMate.Api.Localization;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.CustomerTracking;

namespace MealMate.ApiTests.Controllers;

/// <summary>
/// Verifies the real API JSON contract needed by clients that switch language without
/// refetching: X-Bilingual keeps both source fields, while the default response remains
/// collapsed to the active request language.
/// </summary>
public sealed class CustomerDeliveryLocalizedContractTests
{
    private static CustomerDeliveryWidgetItemDto Widget(string selectedText) => new(
        SubscriptionId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        SubscriptionDayId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        OrderId: null,
        TripStopId: null,
        OrderCode: null,
        DeliveryStatus: CustomerDeliveryStatusCodes.ReadyForPickup,
        DeliveryStatusTextAr: "تم التجهيز",
        DeliveryStatusTextEn: "Ready for pickup",
        DeliveryStatusText: selectedText,
        IsTerminal: false,
        IsTrackable: false,
        UnavailableReasonCode: CustomerDeliveryUnavailableReasons.DriverNotAssigned,
        ReadyAtUtc: null,
        PickedUpAtUtc: null,
        ArrivedAtUtc: null,
        DeliveredAtUtc: null,
        Driver: new CustomerDeliveryDriverDto(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            "سائق",
            "Driver",
            "Driver",
            null,
            null,
            null),
        Vehicle: null,
        Location: null,
        EtaMinutes: null,
        EtaSource: null,
        DestinationAddress: null,
        DetailsUrl: null)
    {
        DeliveryDetailsUrl = "/api/v1/customer/subscription-days/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb/delivery-details"
    };

    [Theory]
    [InlineData("ar", "تم التجهيز", "deliveryStatusTextAr", "deliveryStatusTextEn")]
    [InlineData("en-US", "Ready for pickup", "deliveryStatusTextEn", "deliveryStatusTextAr")]
    public void Default_widget_and_details_json_follow_active_language(
        string cultureName,
        string selectedStatus,
        string selectedField,
        string omittedField)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
        try
        {
            var item = Widget(selectedStatus);
            var details = new CustomerDeliveryDetailsResponseDto(
                new DateOnly(2026, 10, 10),
                new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc),
                CustomerDeliveryWidgetDefaults.RefreshSeconds,
                item);

            var isArabic = cultureName == "ar";
            AssertDefaultLanguageShape(JsonSerializer.Serialize(item, CreateApiJsonOptions()), selectedStatus, selectedField, omittedField, isArabic);
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(details, CreateApiJsonOptions()));
            var delivery = doc.RootElement.GetProperty("delivery");
            delivery.GetProperty("deliveryStatusText").GetString().Should().Be(selectedStatus);
            delivery.GetProperty(selectedField).Should().NotBeNull();
            delivery.TryGetProperty(omittedField, out _).Should().BeFalse();
            delivery.GetProperty("driver").GetProperty("name").GetString().Should().Be(isArabic ? "سائق" : "Driver");
            delivery.GetProperty("driver").TryGetProperty(isArabic ? "nameEn" : "nameAr", out _).Should().BeFalse();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Bilingual_scope_keeps_both_status_and_driver_name_sources_for_widget_and_details()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var item = Widget("Ready for pickup");
            var details = new CustomerDeliveryDetailsResponseDto(
                new DateOnly(2026, 10, 10),
                new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc),
                CustomerDeliveryWidgetDefaults.RefreshSeconds,
                item);

            using var scope = LocalizedJsonScope.Begin(includeSourceFields: true);
            AssertBilingualShape(JsonSerializer.Serialize(item, CreateApiJsonOptions()));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(details, CreateApiJsonOptions()));
            AssertBilingualItem(doc.RootElement.GetProperty("delivery"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static void AssertDefaultLanguageShape(
        string json,
        string selectedStatus,
        string selectedField,
        string omittedField,
        bool isArabic)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("deliveryStatusText").GetString().Should().Be(selectedStatus);
        root.GetProperty(selectedField).Should().NotBeNull();
        root.TryGetProperty(omittedField, out _).Should().BeFalse();
        var driver = root.GetProperty("driver");
        driver.GetProperty("name").GetString().Should().Be(isArabic ? "سائق" : "Driver");
        driver.GetProperty(isArabic ? "nameAr" : "nameEn").Should().NotBeNull();
        driver.TryGetProperty(isArabic ? "nameEn" : "nameAr", out _).Should().BeFalse();
    }

    private static void AssertBilingualShape(string json)
    {
        using var doc = JsonDocument.Parse(json);
        AssertBilingualItem(doc.RootElement);
    }

    private static void AssertBilingualItem(JsonElement root)
    {
        root.GetProperty("deliveryStatusTextAr").GetString().Should().Be("تم التجهيز");
        root.GetProperty("deliveryStatusTextEn").GetString().Should().Be("Ready for pickup");
        root.TryGetProperty("deliveryStatusText", out _).Should().BeFalse();
        var driver = root.GetProperty("driver");
        driver.GetProperty("nameAr").GetString().Should().Be("سائق");
        driver.GetProperty("nameEn").GetString().Should().Be("Driver");
        driver.TryGetProperty("name", out _).Should().BeFalse();
    }

    private static JsonSerializerOptions CreateApiJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        MealMateJsonSerializer.Configure(options);
        return options;
    }
}
