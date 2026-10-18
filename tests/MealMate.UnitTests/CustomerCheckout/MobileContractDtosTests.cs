using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Features.CustomerCheckout;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class MobileContractDtosTests
{
    [Fact]
    public void Money_breakdown_keeps_three_decimal_contract_values()
    {
        var dto = new MobileMoneyBreakdownDto(10.1234m, 1.1111m, 0m, 0.150m, 9.1623m);

        Math.Round(dto.Subtotal, 3).Should().Be(10.123m);
        dto.CurrencyCode.Should().Be("KWD");
    }

    [Fact]
    public void Blocking_reason_contains_machine_code_and_retry_advice()
    {
        var dto = new MobileBlockingReasonDto(
            "checkout.address_required", "العنوان مطلوب", "Address is required", "address",
            new MobileRetryAdviceDto(false, RefreshSummaryRequired: true));

        dto.Code.Should().Be("checkout.address_required");
        dto.Advice!.RefreshSummaryRequired.Should().BeTrue();
        JsonSerializer.Serialize(dto).Should().Contain("checkout.address_required");
    }

    [Fact]
    public void Money_breakdown_maps_legacy_and_new_names_without_double_counting()
    {
        var dto = new MobileMoneyBreakdownDto(100m, 15m, 0m, 2m, 87m,
            CampaignDiscount: 10m, PromoDiscount: 5m);

        dto.LegacyBasePrice.Should().Be(100m);
        dto.LegacyDiscount.Should().Be(15m);
    }
}
