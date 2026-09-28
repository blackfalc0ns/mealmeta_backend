using FluentAssertions;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Xunit;

namespace MealMate.UnitTests.Domain;

public class MealBarcodeCanonicalTests
{
    [Fact]
    public void MealBarcode_HasCanonicalLifecycleProperties_And_DefaultValues()
    {
        var barcode = new MealBarcode
        {
            BarcodeValue = "MM-20260928-8F12C9B4E5A74D2CB3941865A7F90123",
            SubscriptionDayId = Guid.NewGuid(),
            GeneratedAtUtc = DateTime.UtcNow
        };

        barcode.Status.Should().Be(MealBarcodeStatus.Generated);
        barcode.IsRevoked.Should().BeFalse();
        barcode.PrintedAtUtc.Should().BeNull();
        barcode.ScannedAtUtc.Should().BeNull();
        barcode.RevokedAtUtc.Should().BeNull();
        barcode.RevokedBy.Should().BeNull();
        barcode.RevocationReason.Should().BeNull();
        barcode.ReissuedBarcodeId.Should().BeNull();
        barcode.TripStopId.Should().BeNull();
    }

    [Fact]
    public void TripStop_HasMealBarcodeId_And_CanReferenceCanonicalMealBarcode()
    {
        var barcode = new MealBarcode
        {
            BarcodeValue = "MM-20260928-8F12C9B4E5A74D2CB3941865A7F90123",
            SubscriptionDayId = Guid.NewGuid(),
            GeneratedAtUtc = DateTime.UtcNow
        };

        var stop = new TripStop
        {
            TripId = Guid.NewGuid(),
            OrderCode = "#BX-1001",
            CustomerMaskedId = "C-99",
            ZoneNameAr = "حي الصحافة",
            ZoneNameEn = "Al Sahafa",
            MealSummaryAr = "وجبة صحية",
            MealSummaryEn = "Healthy Meal",
            SlotLabel = "الغداء",
            MealBarcodeId = barcode.Id,
            MealBarcode = barcode
        };

        stop.MealBarcodeId.Should().Be(barcode.Id);
        stop.MealBarcode.Should().BeSameAs(barcode);
    }
}
