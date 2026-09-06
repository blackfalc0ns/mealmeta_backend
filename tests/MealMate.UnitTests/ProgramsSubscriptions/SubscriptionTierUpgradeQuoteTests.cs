using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public class SubscriptionTierUpgradeQuoteTests
{
    [Fact]
    public void Prorates_target_preview_minus_paid_credit()
    {
        var target = new PricingEngineResult(
            A_t: 4m,
            DurationDays: 26,
            DurationMultiplierR: 0.15m,
            UnroundedCorePrice: 100m,
            CampaignDiscountApplied: 0m,
            PromoCodeDiscountApplied: 0m,
            WalletCreditApplied: 0m,
            PriceAfterDiscounts: 100m,
            GatewayCostRecoveryAmount: 2.600m,
            FinalCustomerPrice: 130.000m,
            TotalSupplyCost: 0m,
            EstimatedPlatformMargin: 0m,
            EstimatedPlatformMarginPct: 0m,
            IsProfitabilityAlertTriggered: false);

        var quote = SubscriptionTierUpgradeQuote.Calculate(
            paidAmount: 100.000m,
            remainingServiceDays: 16,
            totalServiceDays: 26,
            targetPreview: target);

        Assert.Equal(16, quote.RemainingServiceDays);
        Assert.Equal(61.538m, quote.CurrentRemainingCredit);
        Assert.Equal(80.000m, quote.TargetRemainingPrice);
        Assert.Equal(0m, quote.GatewayRecovery);
        Assert.Equal(18.462m, quote.TotalDueNow);
        Assert.True(quote.RequiresPayment);
    }

    [Fact]
    public void Zero_or_negative_delta_does_not_require_payment()
    {
        var target = new PricingEngineResult(
            A_t: 4m,
            DurationDays: 26,
            DurationMultiplierR: 0.15m,
            UnroundedCorePrice: 50m,
            CampaignDiscountApplied: 0m,
            PromoCodeDiscountApplied: 0m,
            WalletCreditApplied: 0m,
            PriceAfterDiscounts: 50m,
            GatewayCostRecoveryAmount: 0m,
            FinalCustomerPrice: 50.000m,
            TotalSupplyCost: 0m,
            EstimatedPlatformMargin: 0m,
            EstimatedPlatformMarginPct: 0m,
            IsProfitabilityAlertTriggered: false);

        var quote = SubscriptionTierUpgradeQuote.Calculate(100m, 10, 26, target);
        Assert.Equal(0m, quote.TotalDueNow);
        Assert.False(quote.RequiresPayment);
    }
}
