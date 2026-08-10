using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions.Pricing;

public sealed record PricingEngineRequest(
    decimal MeanReferenceDailyPrice, // A_t
    int DurationDays,
    decimal RMax = 0.15m,
    decimal RMin = 0.05m,
    int DShort = 6,
    int DLong = 26,
    decimal Gamma = 1.0m,
    decimal GatewayFeePct = 0.025m,
    decimal GatewayFeeFixedKwd = 0.150m,
    decimal RoundingStepKwd = 0.250m,
    decimal CampaignDiscountKwd = 0m,
    decimal PromoCodeDiscountKwd = 0m,
    decimal WalletPromoCreditKwd = 0m,
    CampaignFundingMode FundingMode = CampaignFundingMode.MealMateFull,
    decimal ContractedSupplyDailyPrice = 0m,
    IReadOnlyDictionary<int, decimal>? DurationRatesPct = null,
    int FridayDays = 0,
    decimal FridayRatePct = 0m);

public sealed record PricingEngineResult(
    decimal A_t,
    int DurationDays,
    decimal DurationMultiplierR,
    decimal UnroundedCorePrice,
    decimal CampaignDiscountApplied,
    decimal PromoCodeDiscountApplied,
    decimal WalletCreditApplied,
    decimal PriceAfterDiscounts,
    decimal GatewayCostRecoveryAmount,
    decimal FinalCustomerPrice,
    decimal TotalSupplyCost,
    decimal EstimatedPlatformMargin,
    decimal EstimatedPlatformMarginPct,
    bool IsProfitabilityAlertTriggered,
    string CurrencyCode = "KWD");

public static class PricingEngine
{
    public static PricingEngineResult Calculate(PricingEngineRequest request)
    {
        var d = Math.Max(6, request.DurationDays);
        var fridayDays = Math.Max(0, request.FridayDays);
        var pricedServiceDays = d + fridayDays;
        var rMax = request.RMax;
        var rMin = request.RMin;
        var dShort = request.DShort;
        var dLong = request.DLong;
        var gamma = request.Gamma;

        // 1. Calculate duration multiplier r(D)
        decimal rD;
        if (request.DurationRatesPct is not null && request.DurationRatesPct.TryGetValue(d, out var configuredRate))
        {
            rD = Math.Clamp(configuredRate / 100m, 0m, 1m);
        }
        else if (d >= dLong)
        {
            rD = rMin;
        }
        else if (d <= dShort)
        {
            rD = rMax;
        }
        else
        {
            var fraction = (decimal)Math.Pow((double)(dLong - d) / (double)(dLong - dShort), (double)gamma);
            rD = rMin + (rMax - rMin) * fraction;
        }

        // 2. Unrounded Core Price = A_t * Duration * (1 + r(D))
        var fridayRate = Math.Clamp(request.FridayRatePct / 100m, 0m, 1m);
        var unroundedCore = request.MeanReferenceDailyPrice * d * (1m + rD)
            + request.MeanReferenceDailyPrice * fridayDays * (1m + fridayRate);

        // 3. Discount ordering sequence
        var campaignDiscount = Math.Min(unroundedCore, request.CampaignDiscountKwd);
        var priceAfterCampaign = Math.Max(0m, unroundedCore - campaignDiscount);

        var promoDiscount = Math.Min(priceAfterCampaign, request.PromoCodeDiscountKwd);
        var priceAfterPromo = Math.Max(0m, priceAfterCampaign - promoDiscount);

        var walletCredit = Math.Min(priceAfterPromo, request.WalletPromoCreditKwd);
        var priceAfterDiscounts = Math.Max(0m, priceAfterPromo - walletCredit);

        // 4. Gateway Cost Recovery
        decimal grossWithGateway = 0m;
        if (priceAfterDiscounts > 0m)
        {
            var feePct = Math.Clamp(request.GatewayFeePct, 0m, 0.50m);
            grossWithGateway = (priceAfterDiscounts + request.GatewayFeeFixedKwd) / (1m - feePct);
        }

        // 5. Final Rounding (RoundUp to RoundingStepKwd)
        var step = request.RoundingStepKwd > 0m ? request.RoundingStepKwd : 0.250m;
        var finalCustomerPrice = Math.Ceiling(grossWithGateway / step) * step;

        var gatewayFeeAmount = grossWithGateway - priceAfterDiscounts;

        // 6. Platform margin calculation & profitability guardrail
        var totalSupplyCost = request.ContractedSupplyDailyPrice * pricedServiceDays;
        var estimatedPlatformMargin = finalCustomerPrice - totalSupplyCost - gatewayFeeAmount;
        var estimatedPlatformMarginPct = finalCustomerPrice > 0m ? (estimatedPlatformMargin / finalCustomerPrice) : 0m;

        // Guardrail alert triggers if estimated margin percentage < 5% (and total customer price > 0)
        var isProfitabilityAlertTriggered = finalCustomerPrice > 0m && estimatedPlatformMarginPct < 0.05m;

        return new PricingEngineResult(
            A_t: request.MeanReferenceDailyPrice,
            DurationDays: pricedServiceDays,
            DurationMultiplierR: Math.Round(rD, 6),
            UnroundedCorePrice: Math.Round(unroundedCore, 3),
            CampaignDiscountApplied: Math.Round(campaignDiscount, 3),
            PromoCodeDiscountApplied: Math.Round(promoDiscount, 3),
            WalletCreditApplied: Math.Round(walletCredit, 3),
            PriceAfterDiscounts: Math.Round(priceAfterDiscounts, 3),
            GatewayCostRecoveryAmount: Math.Round(gatewayFeeAmount, 3),
            FinalCustomerPrice: Math.Round(finalCustomerPrice, 3),
            TotalSupplyCost: Math.Round(totalSupplyCost, 3),
            EstimatedPlatformMargin: Math.Round(estimatedPlatformMargin, 3),
            EstimatedPlatformMarginPct: Math.Round(estimatedPlatformMarginPct, 6),
            IsProfitabilityAlertTriggered: isProfitabilityAlertTriggered);
    }
}
