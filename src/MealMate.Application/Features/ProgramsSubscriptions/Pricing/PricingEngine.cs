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
    decimal FridayRatePct = 0m,
    decimal CashWalletTenderKwd = 0m,
    decimal? MinimumContributionKwd = null);

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
    string CurrencyCode = "KWD",
    decimal ListPrice = 0m,
    decimal CustomerNetPrice = 0m,
    decimal ExternalCaptureAmount = 0m,
    decimal ExpectedGatewayCost = 0m,
    decimal CashWalletTenderApplied = 0m,
    bool? ContributionBelowMinimum = null);

public static class PricingEngine
{
    public static PricingEngineResult Calculate(PricingEngineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CashWalletTenderKwd != 0m)
            throw new NotSupportedException("Cash wallet tender requires an authorized settlement flow.");

        var discountSources = new[]
        {
            request.CampaignDiscountKwd,
            request.PromoCodeDiscountKwd,
            request.WalletPromoCreditKwd,
        };
        if (discountSources.Any(x => x < 0m))
            throw new ArgumentOutOfRangeException(nameof(request), "Discount amounts cannot be negative.");
        if (discountSources.Count(x => x > 0m) > 1)
            throw new ArgumentException("Only one campaign, promo code, or promotional wallet discount may be selected.", nameof(request));

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
            var fraction = DecimalPricingMath.Pow((decimal)(dLong - d) / (dLong - dShort), gamma);
            rD = rMin + (rMax - rMin) * fraction;
        }

        // 2. Unrounded Core Price = A_t * Duration * (1 + r(D))
        var fridayRate = Math.Clamp(request.FridayRatePct / 100m, 0m, 1m);
        var unroundedCore = request.MeanReferenceDailyPrice * d * (1m + rD)
            + request.MeanReferenceDailyPrice * fridayDays * (1m + fridayRate);

        // The list amount is established before a discount or a payment cost is evaluated.
        var step = request.RoundingStepKwd > 0m ? request.RoundingStepKwd : 0.250m;
        var listPrice = unroundedCore <= 0m ? 0m : Math.Ceiling(unroundedCore / step) * step;
        var selectedDiscount = discountSources.SingleOrDefault(x => x > 0m);
        var appliedDiscount = Math.Min(listPrice, selectedDiscount);
        var priceAfterDiscounts = Math.Max(0m, listPrice - appliedDiscount);
        var campaignDiscount = request.CampaignDiscountKwd > 0m ? appliedDiscount : 0m;
        var promoDiscount = request.PromoCodeDiscountKwd > 0m ? appliedDiscount : 0m;
        var walletCredit = request.WalletPromoCreditKwd > 0m ? appliedDiscount : 0m;

        // Gateway charge is an estimate of actual processing cost, never a customer-price recovery.
        var externalCaptureAmount = priceAfterDiscounts;
        var expectedGatewayCost = externalCaptureAmount > 0m
            ? (externalCaptureAmount * Math.Clamp(request.GatewayFeePct, 0m, 0.50m)) + Math.Max(0m, request.GatewayFeeFixedKwd)
            : 0m;

        // Contribution is an estimate; a policy threshold is optional and must be supplied by governance.
        var totalSupplyCost = request.ContractedSupplyDailyPrice * pricedServiceDays;
        var estimatedPlatformMargin = priceAfterDiscounts - totalSupplyCost - expectedGatewayCost;
        var estimatedPlatformMarginPct = priceAfterDiscounts > 0m ? (estimatedPlatformMargin / priceAfterDiscounts) : 0m;
        var contributionBelowMinimum = request.MinimumContributionKwd is decimal minimum
            ? estimatedPlatformMargin < minimum
            : (bool?)null;

        return new PricingEngineResult(
            A_t: request.MeanReferenceDailyPrice,
            DurationDays: pricedServiceDays,
            DurationMultiplierR: Math.Round(rD, 6),
            UnroundedCorePrice: Math.Round(unroundedCore, 3),
            CampaignDiscountApplied: Math.Round(campaignDiscount, 3),
            PromoCodeDiscountApplied: Math.Round(promoDiscount, 3),
            WalletCreditApplied: Math.Round(walletCredit, 3),
            PriceAfterDiscounts: Math.Round(priceAfterDiscounts, 3),
            GatewayCostRecoveryAmount: 0m,
            FinalCustomerPrice: Math.Round(priceAfterDiscounts, 3),
            TotalSupplyCost: Math.Round(totalSupplyCost, 3),
            EstimatedPlatformMargin: Math.Round(estimatedPlatformMargin, 3),
            EstimatedPlatformMarginPct: Math.Round(estimatedPlatformMarginPct, 6),
            IsProfitabilityAlertTriggered: contributionBelowMinimum ?? false,
            ListPrice: Math.Round(listPrice, 3),
            CustomerNetPrice: Math.Round(priceAfterDiscounts, 3),
            ExternalCaptureAmount: Math.Round(externalCaptureAmount, 3),
            ExpectedGatewayCost: expectedGatewayCost,
            CashWalletTenderApplied: 0m,
            ContributionBelowMinimum: contributionBelowMinimum);
    }
}
