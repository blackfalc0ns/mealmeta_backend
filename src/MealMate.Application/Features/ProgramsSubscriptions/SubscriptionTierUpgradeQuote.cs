using MealMate.Application.Features.ProgramsSubscriptions.Pricing;

namespace MealMate.Application.Features.ProgramsSubscriptions;

public sealed record SubscriptionTierUpgradeQuoteResult(
    int RemainingServiceDays,
    int TotalServiceDays,
    decimal CurrentRemainingCredit,
    decimal TargetRemainingPrice,
    decimal GatewayRecovery,
    decimal TotalDueNow,
    bool RequiresPayment);

public static class SubscriptionTierUpgradeQuote
{
    public static SubscriptionTierUpgradeQuoteResult Calculate(
        decimal paidAmount,
        int remainingServiceDays,
        int totalServiceDays,
        PricingEngineResult targetPreview)
    {
        var remaining = Math.Max(0, remainingServiceDays);
        var total = Math.Max(1, totalServiceDays);
        var currentRemaining = Math.Round(paidAmount * remaining / total, 3, MidpointRounding.AwayFromZero);
        var targetRemaining = Math.Round(targetPreview.FinalCustomerPrice * remaining / total, 3, MidpointRounding.AwayFromZero);
        var gateway = Math.Round(targetPreview.GatewayCostRecoveryAmount * remaining / total, 3, MidpointRounding.AwayFromZero);
        var due = Math.Max(0m, Math.Round(targetRemaining - currentRemaining, 3, MidpointRounding.AwayFromZero));
        return new(remaining, total, currentRemaining, targetRemaining, gateway, due, due > 0m);
    }
}
