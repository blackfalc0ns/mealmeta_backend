using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
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
    string SubscriptionType = "Individual")
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
        Guid excludeSubscriptionId = default,
        string subscriptionType = "Individual");

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
        string subscriptionType = "Individual")
    {
        var code = (promoCode ?? string.Empty).Trim().ToUpper();
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
                MessageAr(SubscriptionCouponStatuses.Valid), MessageEn(SubscriptionCouponStatuses.Valid), null, adminCode.Id, adminCode, subscriptionType);
        }

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
        if (redemption is null) return;

        var offer = await _db.Offers.FirstOrDefaultAsync(x => x.Id == redemption.OfferId, ct);
        if (offer is not null && offer.CurrentUsageCount > 0)
            offer.CurrentUsageCount--;

        _db.OfferRedemptions.Remove(redemption);
        await _db.SaveChangesAsync(ct);
    }

    public async Task CreateCheckoutHoldAsync(SubscriptionCouponEvaluation evaluation, Guid subscriptionId,
        Guid customerUserId, decimal discountAmount, CancellationToken ct = default)
    {
        if (evaluation.PromoCodeId is not { } promoCodeId)
        {
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
        => new(code, offerId, status, 0m, MessageAr(status), MessageEn(status), null);

    private static SubscriptionCouponEvaluation RejectedAdmin(string code, PromoCode promo, string status)
        => new(code, null, status, 0m, MessageAr(status), MessageEn(status), null, promo.Id, promo);

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
