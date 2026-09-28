using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class DriverPickupControllerContractTests
{
    [Fact]
    public void ValidateBarcodeResponseDto_SerializesExpectedContractProperties()
    {
        // Arrange
        var boxId = Guid.NewGuid();
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(5);
        var boxDto = new ValidatePickupBoxDto(
            BoxId: boxId,
            BoxCode: "#BX-1001",
            CustomerName: "سالم المنصوري",
            DeliveryZone: "الرياض - العليا",
            MealsCount: 2);

        var dto = new ValidatePickupBarcodeResponseDto(
            IsValid: true,
            ValidationToken: "test-token-abcdef",
            ExpiresAtUtc: expiresAtUtc,
            Box: boxDto,
            ScanStatus: "BarcodeValidated",
            NextAction: "CaptureConditionPhoto");

        // Act
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Assert - Driver mobile client contract properties on root
        root.TryGetProperty("boxId", out var boxIdProp).Should().BeTrue("Contract requires 'boxId' on root");
        boxIdProp.GetString().Should().Be(boxId.ToString());

        root.TryGetProperty("orderCode", out var orderCodeProp).Should().BeTrue("Contract requires 'orderCode' on root");
        orderCodeProp.GetString().Should().Be("#BX-1001");

        root.TryGetProperty("validationToken", out var tokenProp).Should().BeTrue("Contract requires 'validationToken' on root");
        tokenProp.GetString().Should().Be("test-token-abcdef");

        root.TryGetProperty("expiresAtUtc", out var expiresProp).Should().BeTrue("Contract requires 'expiresAtUtc' on root");
        expiresProp.GetString().Should().NotBeNullOrWhiteSpace();

        // Also ensure nested Box is preserved for backward compatibility
        root.TryGetProperty("box", out var nestedBoxProp).Should().BeTrue("Nested 'box' object must be preserved");
        nestedBoxProp.GetProperty("boxCode").GetString().Should().Be("#BX-1001");
    }
}
