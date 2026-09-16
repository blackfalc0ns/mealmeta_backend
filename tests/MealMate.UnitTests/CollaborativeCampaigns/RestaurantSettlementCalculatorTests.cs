using FluentAssertions;
using MealMate.Domain.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns.ValueObjects;
using MealMate.Domain.ValueObjects;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class RestaurantSettlementCalculatorTests
{
    private readonly RestaurantSettlementCalculator _calculator = new();

    [Fact]
    public void CalculateSettlement_StandardSymmetricPricing_CalculatesAccurately()
    {
        // Reference = 60.000 KWD, Base = 60.000 KWD (Spread = 0)
        // RestaurantContribution = 10% (6.000 KWD)
        // Gross Settlement = 54.000 KWD
        // Commission = 15% on 54.000 = 8.100 KWD
        // Net Payout = 54.000 - 8.100 = 45.900 KWD
        var basePrice = new Money(60.000m, "KWD");
        var referencePrice = new Money(60.000m, "KWD");
        var restContrib = Percentage.FromFraction(0.10m);
        var commission = Percentage.FromFraction(0.15m);

        var result = _calculator.CalculateSettlement(basePrice, referencePrice, restContrib, commission);

        result.RestaurantBasePrice.Amount.Should().Be(60.000m);
        result.ReferenceCustomerPrice.Amount.Should().Be(60.000m);
        result.RestaurantDiscountConcession.Amount.Should().Be(6.000m);
        result.GrossRestaurantSettlement.Amount.Should().Be(54.000m);
        result.CommissionAmount.Amount.Should().Be(8.100m);
        result.RestaurantNetPayout.Amount.Should().Be(45.900m);
        result.WholesalePricingSpread.Amount.Should().Be(0.000m);
    }

    [Fact]
    public void CalculateSettlement_WholesaleSpreadScenario_ReportsSpreadAnalytically()
    {
        // Reference Customer Price = 70.000 KWD, Restaurant Base Price = 50.000 KWD
        // Wholesale Spread = 20.000 KWD
        // Restaurant Contribution = 10% of 50.000 = 5.000 KWD
        // Gross = 45.000 KWD
        // Commission = 10% of 45.000 = 4.500 KWD
        // Net = 40.500 KWD
        var basePrice = new Money(50.000m, "KWD");
        var referencePrice = new Money(70.000m, "KWD");
        var restContrib = Percentage.FromFraction(0.10m);
        var commission = Percentage.FromFraction(0.10m);

        var result = _calculator.CalculateSettlement(basePrice, referencePrice, restContrib, commission);

        result.WholesalePricingSpread.Amount.Should().Be(20.000m);
        result.RestaurantDiscountConcession.Amount.Should().Be(5.000m);
        result.GrossRestaurantSettlement.Amount.Should().Be(45.000m);
        result.CommissionAmount.Amount.Should().Be(4.500m);
        result.RestaurantNetPayout.Amount.Should().Be(40.500m);
    }

    [Fact]
    public void CalculateSettlement_Throws_WhenNetPayoutIsNegative()
    {
        // Pathological case: 100% concession + positive commission or negative base
        var basePrice = new Money(10.000m, "KWD");
        var referencePrice = new Money(10.000m, "KWD");
        var restContrib = Percentage.FromFraction(1.00m); // 100% concession -> Gross = 0
        var commission = Percentage.FromFraction(0.20m);

        // When gross is 0, commission is 0, payout is 0 (allowed).
        // But if gross - commission < 0, throws.
        var act = () => _calculator.CalculateSettlement(
            new Money(-5.000m, "KWD"),
            referencePrice,
            Percentage.FromFraction(0.10m),
            commission);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CalculateSettlement_Throws_WhenCommissionRateIsOutOfRange()
    {
        var basePrice = new Money(50.000m, "KWD");
        var referencePrice = new Money(50.000m, "KWD");
        var restContrib = Percentage.FromFraction(0.10m);
        var invalidCommission = Percentage.FromFraction(1.20m); // 120%

        var act = () => _calculator.CalculateSettlement(basePrice, referencePrice, restContrib, invalidCommission);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
