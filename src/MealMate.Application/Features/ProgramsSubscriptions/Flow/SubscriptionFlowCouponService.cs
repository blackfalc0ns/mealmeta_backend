using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.Flow;

/// <summary>Unified coupon evaluation for the subscription flow (summary and checkout share one verdict).</summary>
public sealed record SubscriptionCouponEvaluation(
    string? Code,
    Guid? OfferId,
    string Status,
    decimal DiscountAmount,
    string MessageAr,
    string MessageEn,
    Offer? Offer)
{
    public bool IsValid => Status == SubscriptionCouponStatuses.Valid;
}

public static class SubscriptionCouponStatuses
{
    public const string Valid = "valid";
    public const string NotFound = "not_found";
    public const string Inactive = "inactive";
    public const string NotStarted = "not_started";
    public const string Expired = "expired";
    public const string UsageExhausted = "usage_exhausted";
    public const string StackingConflict = "stacking_conflict";
    public const string Ineligible = "ineligible";
}

public interface ISubscriptionFlowCouponService
{
    /// <summary>Read-only evaluation: never increments usage and never creates a redemption.</summary>
    Task<SubscriptionCouponEvaluation> EvaluateAsync(
        string? promoCode,
        Guid customerUserId,
        Guid programId,
        Guid bundleId,
        int durationDays,
        decimal grossCartAmount,
        CancellationToken ct = default,
        Guid excludeSubscriptionId = default);

    /// <summary>
    /// Records the checkout hold redemption for a subscription inside the checkout transaction.
    /// It is a hold, not a final use: released when payment fails or the checkout expires.
    /// </summary>
    Task CreateCheckoutHoldAsync(Offer offer, Guid subscriptionId, Guid customerUserId, decimal discountAmount, CancellationToken ct = default);

    /// <summary>Removes the checkout hold redemption for a subscription and restores the offer usage count.</summary>
    Task ReleaseCheckoutRedemptionAsync(Guid subscriptionId, CancellationToken ct = default);
}

/// <remarks>
/// The Offers table is the only accepted coupon source: it is what the pricing preview resolves, so summary,
/// checkout and the gateway amount all evaluate the same offer. The legacy PromoCodes table is not consulted.
/// </remarks>
public sealed class SubscriptionFlowCouponService : ISubscriptionFlowCouponService
{
    private readonly IAppDbContext _db;
    private readonly IOfferEligibilityEngine _engine;
    private readonly IDateTimeProvider? _clock;

    public SubscriptionFlowCouponService(IAppDbContext db, IOfferEligibilityEngine engine, IDateTimeProvider? clock = null)
    {
        _db = db;
        _engine = engine;
        _clock = clock;
    }

    public async Task<SubscriptionCouponEvaluation> EvaluateAsync(
        string? promoCode,
        Guid customerUserId,
        Guid programId,
        Guid bundleId,
        int durationDays,
        decimal grossCartAmount,
        CancellationToken ct = default,
        Guid excludeSubscriptionId = default)
    {
        var code = (promoCode ?? string.Empty).Trim().ToUpper();
        if (code.Length == 0)
            return Rejected(null, null, SubscriptionCouponStatuses.NotFound);

        var offer = await _db.Offers.AsNoTracking()
            .Include(o => o.EligiblePrograms)
            .Include(o => o.EligibleBundles)
            .Include(o => o.EligibleRestaurants)
            .FirstOrDefaultAsync(o => o.PromoCode != null && o.PromoCode.Trim().ToUpper() == code, ct);
        if (offer is null)
            return Rejected(code, null, SubscriptionCouponStatuses.NotFound);

        var now = _clock?.UtcNow ?? DateTime.UtcNow;

        // excludeSubscriptionId keeps a resume evaluation fair: the subscription's own hold
        // redemption and its own row in the subscription count must not count against it.
        var customerId = customerUserId;
        var priorSubscriptions = customerId != Guid.Empty
            ? await _db.Subscriptions.AsNoTracking()
                .CountAsync(x => x.CustomerUserId == customerId && x.Id != excludeSubscriptionId, ct)
            : 0;
        var customerRedemptions = customerId != Guid.Empty
            ? await _db.OfferRedemptions.AsNoTracking()
                .CountAsync(x => x.OfferId == offer.Id && x.CustomerId == customerId && x.SubscriptionId != excludeSubscriptionId, ct)
            : 0;

        var context = new OfferEligibilityContext(
            CustomerId: customerId,
            PriorSubscriptionCount: priorSubscriptions,
            CustomerOfferRedemptionCount: customerRedemptions,
            CartAmount: grossCartAmount,
            DurationDays: durationDays,
            DeliveryGovernorate: null,
            ProgramId: programId,
            BundleId: bundleId,
            RestaurantId: null,
            HasPromoCode: true);

        var verdict = _engine.Evaluate(offer, context, now);
        if (verdict.IsEligible)
            return new SubscriptionCouponEvaluation(
                code, offer.Id, SubscriptionCouponStatuses.Valid, verdict.CalculatedDiscountAmount,
                MessageAr(SubscriptionCouponStatuses.Valid), MessageEn(SubscriptionCouponStatuses.Valid), offer);

        return Rejected(code, offer.Id, ClassifyRejection(offer, now, verdict.IneligibilityReasons));
    }

    public async Task CreateCheckoutHoldAsync(Offer offer, Guid subscriptionId, Guid customerUserId, decimal discountAmount, CancellationToken ct = default)
    {
        // Load a tracked instance: the usage increment must persist with the checkout transaction.
        var tracked = await _db.Offers.FirstAsync(x => x.Id == offer.Id, ct);
        var (platformFunded, restaurantFunded) = SplitFunding(tracked, discountAmount);
        _db.OfferRedemptions.Add(new OfferRedemption
        {
            OfferId = tracked.Id,
            CustomerId = customerUserId,
            SubscriptionId = subscriptionId,
            PromoCodeId = null,
            DiscountAppliedAmount = new Money(discountAmount, "KWD"),
            PlatformFundedAmount = new Money(platformFunded, "KWD"),
            RestaurantFundedAmount = new Money(restaurantFunded, "KWD"),
            RedeemedAtUtc = _clock?.UtcNow ?? DateTime.UtcNow,
        });
        tracked.CurrentUsageCount++;
        await _db.SaveChangesAsync(ct);
    }

    public async Task ReleaseCheckoutRedemptionAsync(Guid subscriptionId, CancellationToken ct = default)
    {
        var redemption = await _db.OfferRedemptions
            .FirstOrDefaultAsync(x => x.SubscriptionId == subscriptionId, ct);
        if (redemption is null) return;

        var offer = await _db.Offers.FirstOrDefaultAsync(x => x.Id == redemption.OfferId, ct);
        if (offer is not null && offer.CurrentUsageCount > 0)
            offer.CurrentUsageCount--;

        _db.OfferRedemptions.Remove(redemption);
        await _db.SaveChangesAsync(ct);
    }

    private static (decimal Platform, decimal Restaurant) SplitFunding(Offer offer, decimal discount)
    {
        if (offer.FundingMode == OfferFundingMode.RestaurantFull) return (0m, discount);
        if (offer.FundingMode == OfferFundingMode.Shared
            && offer.PlatformShareRate is { } platformRate && offer.RestaurantShareRate is { } restaurantRate)
        {
            var totalRate = platformRate.Value + restaurantRate.Value;
            if (totalRate > 0m)
            {
                var platform = Math.Round(discount * (platformRate.Value / totalRate), 3, MidpointRounding.AwayFromZero);
                return (platform, discount - platform);
            }
        }
        return (discount, 0m);
    }

    private static string ClassifyRejection(Offer offer, DateTime nowUtc, IReadOnlyList<string> reasons)
    {
        if (nowUtc < offer.StartsAtUtc) return SubscriptionCouponStatuses.NotStarted;
        if (nowUtc > offer.EndsAtUtc) return SubscriptionCouponStatuses.Expired;
        if (offer.Status != OfferStatus.Active) return SubscriptionCouponStatuses.Inactive;

        if (reasons.Any(x =>
                x.Contains("redemption limit", StringComparison.OrdinalIgnoreCase)
                || x.Contains("maximum total redemptions", StringComparison.OrdinalIgnoreCase)))
            return SubscriptionCouponStatuses.UsageExhausted;
        if (reasons.Any(x => x.Contains("stacking", StringComparison.OrdinalIgnoreCase)))
            return SubscriptionCouponStatuses.StackingConflict;
        return SubscriptionCouponStatuses.Ineligible;
    }

    private static SubscriptionCouponEvaluation Rejected(string? code, Guid? offerId, string status)
        => new(code, offerId, status, 0m, MessageAr(status), MessageEn(status), null);

    private static string MessageAr(string status) => status switch
    {
        SubscriptionCouponStatuses.Valid => "تم تطبيق الكوبون بنجاح",
        SubscriptionCouponStatuses.NotFound => "كود الخصم غير موجود",
        SubscriptionCouponStatuses.Inactive => "كود الخصم غير مُفعّل حاليًا",
        SubscriptionCouponStatuses.NotStarted => "لم يبدأ تاريخ صلاحية كود الخصم بعد",
        SubscriptionCouponStatuses.Expired => "انتهت صلاحية كود الخصم",
        SubscriptionCouponStatuses.UsageExhausted => "تم استنفاد عدد مرات استخدام كود الخصم",
        SubscriptionCouponStatuses.StackingConflict => "لا يمكن الجمع بين كود الخصم والعرض الحالي",
        _ => "كود الخصم غير مؤهل لهذا الاشتراك",
    };

    private static string MessageEn(string status) => status switch
    {
        SubscriptionCouponStatuses.Valid => "Coupon applied successfully",
        SubscriptionCouponStatuses.NotFound => "This coupon code does not exist",
        SubscriptionCouponStatuses.Inactive => "This coupon code is not active",
        SubscriptionCouponStatuses.NotStarted => "This coupon code is not valid yet",
        SubscriptionCouponStatuses.Expired => "This coupon code has expired",
        SubscriptionCouponStatuses.UsageExhausted => "This coupon code has reached its usage limit",
        SubscriptionCouponStatuses.StackingConflict => "This coupon code cannot be combined with the current offer",
        _ => "This coupon code is not eligible for this subscription",
    };
}
