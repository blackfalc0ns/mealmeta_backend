using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
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
    Offer? Offer,
    Guid? PromoCodeId = null,
    PromoCode? PromoCode = null,
    string SubscriptionType = "Individual",
    Guid? ReferralCodeId = null,
    ReferralCode? ReferralCode = null)
{
    public bool IsValid => Status == SubscriptionCouponStatuses.Valid;

    public bool IsReferral => Status == SubscriptionCouponStatuses.Valid && ReferralCodeId.HasValue;
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
    public const string SelfReferral = "self_referral";
    public const string AlreadyAttributed = "already_attributed";
    public const string RewardPolicyUnset = "reward_policy_unset";
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
        Guid excludeSubscriptionId = default,
        string subscriptionType = "Individual",
        RestaurantTier? tier = null);

    Task CreateCheckoutHoldAsync(SubscriptionCouponEvaluation evaluation, Guid subscriptionId, Guid customerUserId,
        decimal discountAmount, CancellationToken ct = default);

    /// <summary>
    /// Records the checkout hold redemption for a subscription inside the checkout transaction.
    /// It is a hold, not a final use: released when payment fails or the checkout expires.
    /// </summary>
    Task CreateCheckoutHoldAsync(Offer offer, Guid subscriptionId, Guid customerUserId, decimal discountAmount, CancellationToken ct = default);

    /// <summary>Removes the checkout hold redemption for a subscription and restores the offer usage count.</summary>
    Task ReleaseCheckoutRedemptionAsync(Guid subscriptionId, CancellationToken ct = default);
    Task CommitCheckoutRedemptionAsync(Guid subscriptionId, CancellationToken ct = default);
}

public sealed class SubscriptionFlowCouponService : ISubscriptionFlowCouponService
{
    private readonly IAppDbContext _db;
    private readonly IOfferEligibilityEngine _engine;
    private readonly IDateTimeProvider? _clock;
    private readonly IApplicationLockManager? _locks;

    public SubscriptionFlowCouponService(IAppDbContext db, IOfferEligibilityEngine engine, IDateTimeProvider? clock = null,
        IApplicationLockManager? locks = null)
    {
        _db = db;
        _engine = engine;
        _clock = clock;
        _locks = locks;
    }

    public async Task<SubscriptionCouponEvaluation> EvaluateAsync(
        string? promoCode,
        Guid customerUserId,
        Guid programId,
        Guid bundleId,
        int durationDays,
        decimal grossCartAmount,
        CancellationToken ct = default,
        Guid excludeSubscriptionId = default,
        string subscriptionType = "Individual",
        RestaurantTier? tier = null)
    {
        var code = (promoCode ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0)
            return Rejected(null, null, SubscriptionCouponStatuses.NotFound);

        var adminCode = await _db.PromoCodes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code.ToUpper() == code, ct);
        if (adminCode is not null)
        {
            var adminNow = _clock?.UtcNow ?? DateTime.UtcNow;
            if (!adminCode.IsActive) return RejectedAdmin(code, adminCode, SubscriptionCouponStatuses.Inactive);
            if (adminCode.StartsAtUtc > adminNow) return RejectedAdmin(code, adminCode, SubscriptionCouponStatuses.NotStarted);
            if (adminCode.EndsAtUtc is { } end && end < adminNow) return RejectedAdmin(code, adminCode, SubscriptionCouponStatuses.Expired);
            var ownHold = excludeSubscriptionId != Guid.Empty && await _db.PromoCodeRedemptions.AnyAsync(x =>
                x.PromoCodeId == adminCode.Id && x.SubscriptionId == excludeSubscriptionId
                && x.Status == PromoCodeRedemptionStatus.Held, ct);
            if (adminCode.MaxUses is { } max && adminCode.UsedCount - (ownHold ? 1 : 0) >= max)
                return RejectedAdmin(code, adminCode, SubscriptionCouponStatuses.UsageExhausted);
            var target = ReadTargeting(adminCode);
            if (target is null || (target.CustomerUserId.HasValue && target.CustomerUserId != customerUserId)
                || (target.ProgramId.HasValue && target.ProgramId != programId)
                || (target.BundleId.HasValue && target.BundleId != bundleId)
                || (target.SubscriptionType is { Length: > 0 } type && !string.Equals(type, subscriptionType, StringComparison.Ordinal)))
                return RejectedAdmin(code, adminCode, SubscriptionCouponStatuses.Ineligible);
            if (!IsDurationEligible(adminCode.EligibleDurationDaysJson, durationDays))
                return RejectedAdmin(code, adminCode, SubscriptionCouponStatuses.Ineligible);
            var usedByCustomer = await _db.PromoCodeRedemptions.CountAsync(x => x.PromoCodeId == adminCode.Id
                && x.CustomerId == customerUserId && x.Status != PromoCodeRedemptionStatus.Released
                && x.SubscriptionId != excludeSubscriptionId, ct);
            if (adminCode.PerCustomerLimit is { } customerLimit && usedByCustomer >= customerLimit)
                return RejectedAdmin(code, adminCode, SubscriptionCouponStatuses.UsageExhausted);
            var percent = adminCode.DiscountPercent?.Value;
            var fixedAmount = adminCode.DiscountAmount?.Amount;
            var amount = fixedAmount ?? Math.Round(grossCartAmount * (percent ?? 0m), 3, MidpointRounding.AwayFromZero);
            amount = Math.Min(grossCartAmount, amount);
            if (amount <= 0m) return RejectedAdmin(code, adminCode, SubscriptionCouponStatuses.Ineligible);
            return new SubscriptionCouponEvaluation(code, null, SubscriptionCouponStatuses.Valid, amount,
                SubscriptionCouponMessages.MessageAr(SubscriptionCouponStatuses.Valid), SubscriptionCouponMessages.MessageEn(SubscriptionCouponStatuses.Valid), null, adminCode.Id, adminCode, subscriptionType);
        }

        var offer = await _db.Offers.AsNoTracking()
            .Include(o => o.EligiblePrograms)
            .Include(o => o.EligibleBundles)
            .Include(o => o.PackageTargets)
            .Include(o => o.EligibleRestaurants)
            .FirstOrDefaultAsync(o => o.PromoCode != null && o.PromoCode.Trim().ToUpper() == code, ct);
        if (offer is null)
            return await EvaluateReferralAsync(code, customerUserId, grossCartAmount, ct, excludeSubscriptionId);

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
            HasPromoCode: true,
            Tier: tier,
            SubscriptionType: subscriptionType,
            IsOfferPromoCode: true);

        var verdict = _engine.Evaluate(offer, context, now);
        if (verdict.IsEligible)
            return new SubscriptionCouponEvaluation(
                code, offer.Id, SubscriptionCouponStatuses.Valid, verdict.CalculatedDiscountAmount,
                SubscriptionCouponMessages.MessageAr(SubscriptionCouponStatuses.Valid),
                SubscriptionCouponMessages.MessageEn(SubscriptionCouponStatuses.Valid), offer);

        return Rejected(code, offer.Id, ClassifyRejection(offer, now, verdict.IneligibilityReasons));
    }

    /// <summary>
    /// Referral branch of the coupon chain, evaluated with the same checkout semantics as
    /// admin promo codes: eligibility (owner/used-up/policy), per-customer attribution
    /// (once per customer, resume-safe against the caller's own in-flight checkout) and the
    /// reward percentage applied to the cart amount.
    /// </summary>
    private async Task<SubscriptionCouponEvaluation> EvaluateReferralAsync(
        string code, Guid customerUserId, decimal grossCartAmount,
        CancellationToken ct, Guid excludeSubscriptionId)
    {
        var referralCode = await _db.ReferralCodes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code.ToUpper() == code, ct);
        if (referralCode is null)
            return Rejected(code, null, SubscriptionCouponStatuses.NotFound);
        if (!referralCode.IsActive)
            return RejectedReferral(code, referralCode.Id, SubscriptionCouponStatuses.Inactive);
        if (referralCode.RewardPercent is null)
            return RejectedReferral(code, referralCode.Id, SubscriptionCouponStatuses.RewardPolicyUnset);
        if (referralCode.OwnerUserId == customerUserId)
            return RejectedReferral(code, referralCode.Id, SubscriptionCouponStatuses.SelfReferral);

        // Attribution is unique per referred customer. An attribution attached to the
        // caller's own in-flight checkout is its hold and must not reject a resume evaluation.
        var hasOtherAttribution = excludeSubscriptionId == Guid.Empty
            ? await _db.ReferralAttributions.AsNoTracking()
                .AnyAsync(x => x.ReferredCustomerUserId == customerUserId, ct)
            : await _db.ReferralAttributions.AsNoTracking()
                .AnyAsync(x => x.ReferredCustomerUserId == customerUserId && x.RewardSubscriptionId != excludeSubscriptionId, ct);
        if (hasOtherAttribution)
            return RejectedReferral(code, referralCode.Id, SubscriptionCouponStatuses.AlreadyAttributed);

        var ownHold = excludeSubscriptionId != Guid.Empty && await _db.ReferralAttributions.AsNoTracking()
            .AnyAsync(x => x.RewardSubscriptionId == excludeSubscriptionId, ct);
        if (referralCode.MaxUses is { } max && referralCode.UsedCount - (ownHold ? 1 : 0) >= max)
            return RejectedReferral(code, referralCode.Id, SubscriptionCouponStatuses.UsageExhausted);

        var amount = Math.Min(grossCartAmount, Math.Round(grossCartAmount * referralCode.RewardPercent.Value, 3, MidpointRounding.AwayFromZero));
        if (amount <= 0m)
            return RejectedReferral(code, referralCode.Id, SubscriptionCouponStatuses.Ineligible);

        return new SubscriptionCouponEvaluation(
            code, null, SubscriptionCouponStatuses.Valid, amount,
            SubscriptionCouponMessages.MessageAr(SubscriptionCouponStatuses.Valid, isReferralSource: true),
            SubscriptionCouponMessages.MessageEn(SubscriptionCouponStatuses.Valid, isReferralSource: true),
            null, ReferralCodeId: referralCode.Id, ReferralCode: referralCode);
    }

    public async Task CreateCheckoutHoldAsync(Offer offer, Guid subscriptionId, Guid customerUserId, decimal discountAmount, CancellationToken ct = default)
    {
        // Load a tracked instance: the usage increment must persist with the checkout transaction.
        var tracked = await _db.Offers.FirstAsync(x => x.Id == offer.Id, ct);
        // The eligibility read happens before checkout persistence and can race with another
        // checkout. Re-check the total limit against the freshly loaded row so a caller that
        // observes an already exhausted offer cannot create a second hold.
        if (tracked.TotalUsageLimit is { } totalLimit && tracked.CurrentUsageCount >= totalLimit)
            throw new DbUpdateConcurrencyException("The coupon usage limit was reached by another checkout.");
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
        var redemptionInfo = await _db.PromoCodeRedemptions.AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId).Select(x => new { x.Id, x.PromoCodeId }).FirstOrDefaultAsync(ct);
        if (redemptionInfo is not null)
        {
            if (_locks is not null) await _locks.LockPromoCodeAsync(redemptionInfo.PromoCodeId, ct);
            var promoRedemption = await _db.PromoCodeRedemptions.FirstOrDefaultAsync(x => x.Id == redemptionInfo.Id, ct);
            if (promoRedemption is null) return;
            if (promoRedemption.Status == PromoCodeRedemptionStatus.Released) return;
            var wasReleased = promoRedemption.Release(_clock?.UtcNow ?? DateTime.UtcNow, "cancelled_or_payment_failed");
            if (wasReleased)
            {
                var promoCode = await _db.PromoCodes.FirstOrDefaultAsync(x => x.Id == promoRedemption.PromoCodeId, ct);
                if (promoCode is not null && promoCode.UsedCount > 0) promoCode.UsedCount--;
                await _db.SaveChangesAsync(ct);
            }
            return;
        }
        var redemption = await _db.OfferRedemptions
            .FirstOrDefaultAsync(x => x.SubscriptionId == subscriptionId, ct);
        if (redemption is not null)
        {
            var offer = await _db.Offers.FirstOrDefaultAsync(x => x.Id == redemption.OfferId, ct);
            if (offer is not null && offer.CurrentUsageCount > 0)
                offer.CurrentUsageCount--;

            _db.OfferRedemptions.Remove(redemption);
            await _db.SaveChangesAsync(ct);
            return;
        }

        await ReleaseReferralAttributionAsync(subscriptionId, ct);
    }

    /// <summary>
    /// Referral holds persist a per-customer attribution row instead of a redemption row. A
    /// failed/expired checkout is not an arrival: the attribution is removed and the usage
    /// count restored, exactly like the promo/offer holds above.
    /// </summary>
    private async Task ReleaseReferralAttributionAsync(Guid subscriptionId, CancellationToken ct)
    {
        var attribution = await _db.ReferralAttributions
            .FirstOrDefaultAsync(x => x.RewardSubscriptionId == subscriptionId, ct);
        if (attribution is null) return;

        var referralCode = attribution.ReferralCodeId is { } codeId
            ? await _db.ReferralCodes.FirstOrDefaultAsync(x => x.Id == codeId, ct)
            : null;
        if (referralCode is not null && referralCode.UsedCount > 0) referralCode.UsedCount--;
        _db.ReferralAttributions.Remove(attribution);
        await _db.SaveChangesAsync(ct);
    }

    public async Task CreateCheckoutHoldAsync(SubscriptionCouponEvaluation evaluation, Guid subscriptionId,
        Guid customerUserId, decimal discountAmount, CancellationToken ct = default)
    {
        if (evaluation.PromoCodeId is not { } promoCodeId)
        {
            if (evaluation.ReferralCodeId is { } referralCodeId)
            {
                await CreateReferralHoldAsync(evaluation, referralCodeId, subscriptionId, customerUserId, discountAmount, ct);
                return;
            }
            if (evaluation.Offer is null) throw new InvalidOperationException("A valid coupon evaluation must identify its source.");
            await CreateCheckoutHoldAsync(evaluation.Offer, subscriptionId, customerUserId, discountAmount, ct);
            return;
        }

        var promo = await _db.PromoCodes.FirstAsync(x => x.Id == promoCodeId, ct);
        if (_locks is not null) await _locks.LockPromoCodeAsync(promoCodeId, ct);
        promo = await _db.PromoCodes.FirstAsync(x => x.Id == promoCodeId, ct);
        var now = _clock?.UtcNow ?? DateTime.UtcNow;

        var existing = await _db.PromoCodeRedemptions.FirstOrDefaultAsync(x => x.PromoCodeId == promo.Id && x.SubscriptionId == subscriptionId, ct);
        if (existing is not null)
        {
            if (existing.Status == PromoCodeRedemptionStatus.Held)
            {
                var appliedExisting = Math.Min(discountAmount, evaluation.DiscountAmount);
                if (appliedExisting <= 0m) throw new DbUpdateConcurrencyException("The promo code discount is no longer eligible.");
                existing.DiscountAppliedAmount = new Money(appliedExisting, promo.DiscountAmount?.CurrencyCode ?? "KWD");
                existing.PlatformFundedAmount = new Money(appliedExisting, promo.DiscountAmount?.CurrencyCode ?? "KWD");
                existing.HeldAtUtc = now;
                await _db.SaveChangesAsync(ct);
                return;
            }
            if (existing.Status == PromoCodeRedemptionStatus.Committed)
            {
                return;
            }
        }

        if (!promo.IsActive || promo.StartsAtUtc > now || (promo.EndsAtUtc is { } end && end < now)
            || (promo.MaxUses is { } max && promo.UsedCount >= max))
            throw new DbUpdateConcurrencyException("The promo code is no longer eligible for checkout.");
        var target = ReadTargeting(promo);
        var subscription = await _db.Subscriptions.FirstAsync(x => x.Id == subscriptionId, ct);
        var actualFamilyType = subscription.FamilyGroupId.HasValue ? "Family" : "Individual";
        var familyBundleTypeValid = evaluation.SubscriptionType != "FamilyBundle" ||
            (subscription.FamilyGroupId.HasValue && subscription.FamilyMemberId.HasValue);
        var typeValid = evaluation.SubscriptionType switch
        {
            "Individual" => actualFamilyType == "Individual",
            "Family" => actualFamilyType == "Family",
            "FamilyBundle" => familyBundleTypeValid,
            _ => false,
        };
        if (target is null
            || (target.CustomerUserId.HasValue && target.CustomerUserId != customerUserId)
            || (target.ProgramId.HasValue && target.ProgramId != subscription.ProgramId)
            || (target.BundleId.HasValue && target.BundleId != subscription.BundleId)
            || (!string.IsNullOrWhiteSpace(target.SubscriptionType)
                && !string.Equals(target.SubscriptionType, evaluation.SubscriptionType, StringComparison.Ordinal))
            || !typeValid)
            throw new DbUpdateConcurrencyException("The promo code target no longer matches this subscription.");
        var count = await _db.PromoCodeRedemptions.CountAsync(x => x.PromoCodeId == promo.Id
            && x.CustomerId == customerUserId && x.Status != PromoCodeRedemptionStatus.Released && x.SubscriptionId != subscriptionId, ct);
        if (promo.PerCustomerLimit is { } customerLimit && count >= customerLimit)
            throw new DbUpdateConcurrencyException("The promo code per-customer usage limit was reached.");
        var applied = Math.Min(discountAmount, evaluation.DiscountAmount);
        if (applied <= 0m) throw new DbUpdateConcurrencyException("The promo code discount is no longer eligible.");

        if (existing is not null && existing.Status == PromoCodeRedemptionStatus.Released)
        {
            existing.Rehold(now, new Money(applied, promo.DiscountAmount?.CurrencyCode ?? "KWD"));
        }
        else
        {
            _db.PromoCodeRedemptions.Add(new PromoCodeRedemption
            {
                PromoCodeId = promo.Id,
                CustomerId = customerUserId,
                SubscriptionId = subscriptionId,
                DiscountAppliedAmount = new Money(applied, promo.DiscountAmount?.CurrencyCode ?? "KWD"),
                PlatformFundedAmount = new Money(applied, promo.DiscountAmount?.CurrencyCode ?? "KWD"),
                RestaurantFundedAmount = new Money(0m, promo.DiscountAmount?.CurrencyCode ?? "KWD"),
                DiscountPercentSnapshot = promo.DiscountPercent?.Value,
                DiscountAmountSnapshot = promo.DiscountAmount?.Amount,
                TargetingSnapshotJson = promo.TargetingJson ?? "{}",
                HeldAtUtc = now,
            });
        }
        promo.UsedCount++;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Referral hold: re-checks the policy against the freshly loaded row and upserts the
    /// per-customer attribution (resurrecting a soft-deleted one so the unique index holds),
    /// pins the reward snapshot and counts the hold in ReferralCode.UsedCount.
    /// </summary>
    private async Task CreateReferralHoldAsync(SubscriptionCouponEvaluation evaluation, Guid referralCodeId,
        Guid subscriptionId, Guid customerUserId, decimal discountAmount, CancellationToken ct)
    {
        if (discountAmount <= 0m)
            throw new DbUpdateConcurrencyException("The referral discount is no longer eligible.");
        var now = _clock?.UtcNow ?? DateTime.UtcNow;
        var tracked = await _db.ReferralCodes.FirstAsync(x => x.Id == referralCodeId, ct);
        if (!tracked.IsActive || tracked.RewardPercent is null
            || tracked.OwnerUserId == customerUserId
            || (tracked.MaxUses is { } max && tracked.UsedCount >= max))
            throw new DbUpdateConcurrencyException("The referral code is no longer eligible for checkout.");

        var attribution = await _db.ReferralAttributions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.ReferredCustomerUserId == customerUserId, ct);
        var resurrected = attribution is { DeletedAtUtc: not null };
        if (attribution is not null && !resurrected && attribution.RewardSubscriptionId != subscriptionId)
            throw new DbUpdateConcurrencyException("The customer already used a referral code.");

        if (attribution is not null)
        {
            attribution.Code = tracked.Code;
            attribution.ReferrerUserId = tracked.OwnerUserId;
            attribution.ReferralCodeId = tracked.Id;
            attribution.AttributedAtUtc = now;
            attribution.RewardPercentSnapshot = tracked.RewardPercent.Value;
            attribution.RewardSubscriptionId = subscriptionId;
            attribution.RewardedAtUtc = null;
            attribution.DeletedAtUtc = null;
        }
        else
        {
            _db.ReferralAttributions.Add(new ReferralAttribution
            {
                Code = tracked.Code,
                ReferrerUserId = tracked.OwnerUserId,
                ReferredCustomerUserId = customerUserId,
                AttributedAtUtc = now,
                ReferralCodeId = tracked.Id,
                RewardPercentSnapshot = tracked.RewardPercent.Value,
                RewardSubscriptionId = subscriptionId,
            });
        }
        if (resurrected || attribution is null) tracked.UsedCount++;
        await _db.SaveChangesAsync(ct);
    }

    public async Task CommitCheckoutRedemptionAsync(Guid subscriptionId, CancellationToken ct = default)
    {
        var redemptionInfo = await _db.PromoCodeRedemptions.AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId).Select(x => new { x.Id, x.PromoCodeId }).FirstOrDefaultAsync(ct);
        if (redemptionInfo is null) return;
        if (_locks is not null) await _locks.LockPromoCodeAsync(redemptionInfo.PromoCodeId, ct);
        var redemption = await _db.PromoCodeRedemptions.FirstOrDefaultAsync(x => x.Id == redemptionInfo.Id, ct);
        if (redemption is null) return;
        if (redemption.Commit(_clock?.UtcNow ?? DateTime.UtcNow)) await _db.SaveChangesAsync(ct);
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
        => new(code, offerId, status, 0m, SubscriptionCouponMessages.MessageAr(status), SubscriptionCouponMessages.MessageEn(status), null);

    private static SubscriptionCouponEvaluation RejectedAdmin(string code, PromoCode promo, string status)
        => new(code, null, status, 0m, SubscriptionCouponMessages.MessageAr(status), SubscriptionCouponMessages.MessageEn(status), null, promo.Id, promo);

    private static SubscriptionCouponEvaluation RejectedReferral(string code, Guid referralCodeId, string status)
        => new(code, null, status, 0m, SubscriptionCouponMessages.MessageAr(status, isReferralSource: true),
            SubscriptionCouponMessages.MessageEn(status, isReferralSource: true), null, ReferralCodeId: referralCodeId);

    private static PromoTargeting? ReadTargeting(PromoCode promo)
    {
        if (string.IsNullOrWhiteSpace(promo.TargetingJson)) return new(null, null, null, null);
        try { return System.Text.Json.JsonSerializer.Deserialize<PromoTargeting>(promo.TargetingJson); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static bool IsDurationEligible(string? json, int durationDays)
    {
        if (string.IsNullOrWhiteSpace(json)) return true;
        try { return System.Text.Json.JsonSerializer.Deserialize<HashSet<int>>(json)?.Contains(durationDays) == true; }
        catch (System.Text.Json.JsonException) { return false; }
    }

    private sealed record PromoTargeting(Guid? CustomerUserId, Guid? ProgramId, Guid? BundleId, string? SubscriptionType);
}

/// <summary>
/// One bilingual copy of every coupon/referral status message, shared by the coupon
/// evaluation chain, the checkout gate and the admin discount validation endpoint.
/// </summary>
public static class SubscriptionCouponMessages
{
    public static string MessageAr(string status, bool isReferralSource = false) => status switch
    {
        _ when isReferralSource && status == SubscriptionCouponStatuses.Valid => "تم تطبيق كود الإحالة بنجاح",
        SubscriptionCouponStatuses.Valid => "تم تطبيق الكوبون بنجاح",
        _ when isReferralSource && status == SubscriptionCouponStatuses.NotFound => "كود الإحالة غير موجود",
        SubscriptionCouponStatuses.NotFound => "كود الخصم غير موجود",
        _ when isReferralSource && status == SubscriptionCouponStatuses.Inactive => "كود الإحالة غير مُفعّل حاليًا",
        SubscriptionCouponStatuses.Inactive => "كود الخصم غير مُفعّل حاليًا",
        SubscriptionCouponStatuses.NotStarted => "لم يبدأ تاريخ صلاحية كود الخصم بعد",
        SubscriptionCouponStatuses.Expired => "انتهت صلاحية كود الخصم",
        _ when isReferralSource && status == SubscriptionCouponStatuses.UsageExhausted => "تم استنفاد عدد مرات استخدام كود الإحالة",
        SubscriptionCouponStatuses.UsageExhausted => "تم استنفاد عدد مرات استخدام كود الخصم",
        SubscriptionCouponStatuses.StackingConflict => "لا يمكن الجمع بين كود الخصم والعرض الحالي",
        SubscriptionCouponStatuses.SelfReferral => "لا يمكنك استخدام كود الإحالة الخاص بك",
        SubscriptionCouponStatuses.AlreadyAttributed => "لقد استخدمت كود إحالة من قبل",
        SubscriptionCouponStatuses.RewardPolicyUnset => "لا تستطيع استخدام كود إحالة بدون نسبة جایزة",
        _ => isReferralSource ? "كود الإحالة غير مؤهل لهذا الاشتراك" : "كود الخصم غير مؤهل لهذا الاشتراك",
    };

    public static string MessageEn(string status, bool isReferralSource = false) => status switch
    {
        _ when isReferralSource && status == SubscriptionCouponStatuses.Valid => "Referral code applied successfully",
        SubscriptionCouponStatuses.Valid => "Coupon applied successfully",
        _ when isReferralSource && status == SubscriptionCouponStatuses.NotFound => "This referral code does not exist",
        SubscriptionCouponStatuses.NotFound => "This coupon code does not exist",
        _ when isReferralSource && status == SubscriptionCouponStatuses.Inactive => "This referral code is not active",
        SubscriptionCouponStatuses.Inactive => "This coupon code is not active",
        SubscriptionCouponStatuses.NotStarted => "This coupon code is not valid yet",
        SubscriptionCouponStatuses.Expired => "This coupon code has expired",
        _ when isReferralSource && status == SubscriptionCouponStatuses.UsageExhausted => "This referral code has reached its usage limit",
        SubscriptionCouponStatuses.UsageExhausted => "This coupon code has reached its usage limit",
        SubscriptionCouponStatuses.StackingConflict => "This coupon code cannot be combined with the current offer",
        SubscriptionCouponStatuses.SelfReferral => "You cannot use your own referral code",
        SubscriptionCouponStatuses.AlreadyAttributed => "You have already used a referral code",
        SubscriptionCouponStatuses.RewardPolicyUnset => "This referral code has no reward percentage configured",
        _ => isReferralSource ? "This referral code is not eligible for this subscription" : "This coupon code is not eligible for this subscription",
    };
}
