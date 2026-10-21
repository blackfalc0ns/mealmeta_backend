using FluentAssertions;
using MealMate.Application.Features.ProgramsSubscriptions.Upgrade;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public sealed class UpgradeFinancialTests
{
    [Fact]
    public void ContinuingDiscount_AppliesToDifferenceOnly_Produces22Point5PaymentAnd102Point5Liability()
    {
        // Example from Section 7:
        // oldCredit = 80, newList = 105, grossDiff = 25, continuing 10% on diff = 2.500
        // additional payment = 22.500, new unearned liability = 102.500
        var input = new UpgradeFinancialInput(
            OldCredit: 80.000m,
            NewList: 105.000m,
            ContinuingDiscountRate: 0.10m,
            IsContinuingDiscountEligible: true);

        var result = UpgradeFinancialCalculator.Calculate(input);

        result.GrossDifference.Should().Be(25.000m);
        result.UpgradeDiscount.Should().Be(2.500m);
        result.AdditionalPayment.Should().Be(22.500m);
        result.NewUnearnedLiability.Should().Be(102.500m);
    }

    [Fact]
    public void NonContinuingDiscount_DoesNotCarryOver_Produces25PaymentAnd105Liability()
    {
        // Welcome discount or temporary influencer campaign does not transfer:
        // payment = 25.000, new unearned liability = 105.000
        var input = new UpgradeFinancialInput(
            OldCredit: 80.000m,
            NewList: 105.000m,
            ContinuingDiscountRate: 0.10m,
            IsContinuingDiscountEligible: false);

        var result = UpgradeFinancialCalculator.Calculate(input);

        result.GrossDifference.Should().Be(25.000m);
        result.UpgradeDiscount.Should().Be(0.000m);
        result.AdditionalPayment.Should().Be(25.000m);
        result.NewUnearnedLiability.Should().Be(105.000m);
    }

    [Fact]
    public void NegativeOrZeroDifference_RequiresZeroPayment()
    {
        var input = new UpgradeFinancialInput(
            OldCredit: 100.000m,
            NewList: 80.000m,
            ContinuingDiscountRate: 0.10m,
            IsContinuingDiscountEligible: true);

        var result = UpgradeFinancialCalculator.Calculate(input);

        result.GrossDifference.Should().Be(0.000m);
        result.UpgradeDiscount.Should().Be(0.000m);
        result.AdditionalPayment.Should().Be(0.000m);
        result.NewUnearnedLiability.Should().Be(100.000m);
    }
}
