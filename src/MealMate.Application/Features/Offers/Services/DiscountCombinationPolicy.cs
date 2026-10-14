namespace MealMate.Application.Features.Offers.Services;

/// <summary>
/// Server-side evaluation of how campaign, coupon and referral discounts combine.
/// Amounts are server-computed contributions — never client-supplied totals.
/// </summary>
public sealed record DiscountCombinationRequest(
    decimal BaseAmount,
    decimal CampaignAmount,
    decimal CouponAmount,
    decimal ReferralAmount,
    string CurrencyCode,
    bool AllowStacking);

public sealed record DiscountCombinationResult(
    bool IsAllowed,
    decimal FinalAmount,
    string CurrencyCode,
    string? ReasonCode);

/// <summary>Stable reason identifiers for discount combination outcomes.</summary>
public static class DiscountCombinationReasons
{
    public const string Allowed = "allowed";
    public const string StackingNotAllowed = "STACKING_NOT_ALLOWED";
    public const string ClampedToZero = "clamped_to_zero";
}

public sealed class DiscountCombinationPolicy
{
    /// <summary>
    /// Combines the server-computed discount contributions into one final amount.
    /// Never returns a negative result; stacking conflicts are refused, not silently resolved.
    /// </summary>
    public DiscountCombinationResult Evaluate(DiscountCombinationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.BaseAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Base amount cannot be negative.");
        }

        var campaign = Math.Max(0m, request.CampaignAmount);
        var coupon = Math.Max(0m, request.CouponAmount);
        var referral = Math.Max(0m, request.ReferralAmount);

        var additionalDiscount = coupon + referral;
        if (campaign > 0m && additionalDiscount > 0m && !request.AllowStacking)
        {
            // Campaign price stands; extra discounts are refused without touching the campaign price.
            return new DiscountCombinationResult(
                IsAllowed: false,
                FinalAmount: Math.Max(0m, request.BaseAmount - campaign),
                CurrencyCode: request.CurrencyCode,
                ReasonCode: DiscountCombinationReasons.StackingNotAllowed);
        }

        var final = request.BaseAmount - campaign - coupon - referral;
        if (final < 0m)
        {
            return new DiscountCombinationResult(
                IsAllowed: true,
                FinalAmount: 0m,
                CurrencyCode: request.CurrencyCode,
                ReasonCode: DiscountCombinationReasons.ClampedToZero);
        }

        return new DiscountCombinationResult(
            IsAllowed: true,
            FinalAmount: final,
            CurrencyCode: request.CurrencyCode,
            ReasonCode: DiscountCombinationReasons.Allowed);
    }
}

/// <summary>
/// Converts a legacy PromoCode entity into a server-computed contribution for the
/// combination policy while preserving the legacy record and its identity.
/// </summary>
public sealed class LegacyPromoCodeAdapter
{
    public decimal ComputeContribution(Domain.ProgramsSubscriptions.PromoCode promo, decimal serverBaseAmount)
    {
        ArgumentNullException.ThrowIfNull(promo);

        if (serverBaseAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(serverBaseAmount), "Base amount cannot be negative.");
        }

        if (promo.DiscountPercent is not null)
        {
            var capped = Math.Min(1m, Math.Max(0m, promo.DiscountPercent.Value));
            return Math.Round(serverBaseAmount * capped, 3, MidpointRounding.AwayFromZero);
        }

        if (promo.DiscountAmount is not null)
        {
            return Math.Min(serverBaseAmount, Math.Max(0m, promo.DiscountAmount.Amount));
        }

        // A promo code without a configured rule contributes nothing — never an invented discount.
        return 0m;
    }
}
