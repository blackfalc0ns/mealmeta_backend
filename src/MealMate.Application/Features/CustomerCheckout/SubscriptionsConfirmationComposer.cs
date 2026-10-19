using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.Offers;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MealMate.Application.Features.CustomerCheckout;

public sealed record GetSubscriptionConfirmationQuery(Guid SubscriptionId, Guid CallerUserId)
    : IQuery<SubscriptionCheckoutConfirmationDto>;

public sealed record SubscriptionCheckoutConfirmationDto(
    Guid SubscriptionId,
    string Status,
    bool IsPaid,
    bool IsFailed,
    Guid ProgramId,
    string ProgramNameAr,
    string ProgramNameEn,
    Guid BundleId,
    string BundleNameAr,
    string BundleNameEn,
    RestaurantTier Tier,
    string TierNameAr,
    string TierNameEn,
    int DurationDays,
    bool IncludeFriday,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal? AmountPaidKwd,
    string CurrencyCode,
    DateTime? PaymentVerifiedAtUtc,
    SubscriptionDiscountConfirmationDto? Discount,
    ReferralShareMiniDto? ReferralShare);

public sealed record SubscriptionDiscountConfirmationDto(
    Guid? PromoCodeId,
    string? PromoCode,
    Guid? OfferId,
    string? OfferCode,
    string? Source,
    decimal DiscountKwd);

public sealed record ReferralShareMiniDto(
    bool HasRewardPolicy,
    decimal? RewardPercent,
    string? Code,
    string? ShareUrl);

/// <summary>Read-only composer for screen 12.05: one stable confirmation payload across payment states.</summary>
public sealed class SubscriptionsConfirmationComposer : IQueryHandler<GetSubscriptionConfirmationQuery, SubscriptionCheckoutConfirmationDto>
{
    private readonly IAppDbContext _db;
    private readonly IConfiguration _configuration;

    public SubscriptionsConfirmationComposer(IAppDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<Result<SubscriptionCheckoutConfirmationDto>> Handle(GetSubscriptionConfirmationQuery request, CancellationToken cancellationToken)
    {
        var ownedViaFamily = await _db.FamilyGroups.AsNoTracking()
            .AnyAsync(g => g.ManagerCustomerUserId == request.CallerUserId, cancellationToken);
        var subscription = await _db.Subscriptions.AsNoTracking()
            .Include(x => x.Program)
            .Include(x => x.Bundle)
            .Include(x => x.PromoCode)
            .Include(x => x.DiscountsAndReferralLogs)
            .FirstOrDefaultAsync(x => x.Id == request.SubscriptionId
                && (x.CustomerUserId == request.CallerUserId
                    || (ownedViaFamily && x.FamilyGroupId != null
                        && _db.FamilyGroups.Any(g => g.Id == x.FamilyGroupId && g.ManagerCustomerUserId == request.CallerUserId))),
                cancellationToken);
        if (subscription is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Subscription not found.");

        var pricingRow = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, cancellationToken);
        var tierLabels = SubscriptionPricingSettingsReader.Deserialize(pricingRow?.ValueJson).TierLabels;
        var (tierNameAr, tierNameEn) = subscription.SelectedTier switch
        {
            RestaurantTier.Platinum => (tierLabels.PlatinumAr, tierLabels.PlatinumEn),
            RestaurantTier.Elite => (tierLabels.EliteAr, tierLabels.EliteEn),
            _ => (tierLabels.BasicAr, tierLabels.BasicEn)
        };

        var isPaid = subscription.Status is SubscriptionStatus.Paid
            or SubscriptionStatus.Scheduled
            or SubscriptionStatus.Active;
        var isFailed = subscription.Status is SubscriptionStatus.PaymentFailed or SubscriptionStatus.Cancelled;

        var discount = await ComposeDiscountAsync(subscription, cancellationToken);

        var caller = await _db.ReferralCodes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.OwnerUserId == request.CallerUserId, cancellationToken);
        var baseUrl = _configuration["PublicBaseUrl"];
        var referralShare = caller is not null
            ? new ReferralShareMiniDto(
                HasRewardPolicy: caller.RewardPercent is not null,
                RewardPercent: caller.RewardPercent?.Value,
                Code: caller.Code,
                ShareUrl: !string.IsNullOrWhiteSpace(baseUrl) ? $"{baseUrl.TrimEnd('/')}/r/{caller.Code}" : null)
            : null;

        return Result.Success(new SubscriptionCheckoutConfirmationDto(
            SubscriptionId: subscription.Id,
            Status: subscription.Status.ToString(),
            IsPaid: isPaid,
            IsFailed: isFailed,
            ProgramId: subscription.ProgramId,
            ProgramNameAr: subscription.Program?.NameAr ?? string.Empty,
            ProgramNameEn: subscription.Program?.NameEn ?? string.Empty,
            BundleId: subscription.BundleId,
            BundleNameAr: subscription.Bundle?.NameAr ?? string.Empty,
            BundleNameEn: subscription.Bundle?.NameEn ?? string.Empty,
            Tier: subscription.SelectedTier,
            TierNameAr: tierNameAr,
            TierNameEn: tierNameEn,
            DurationDays: subscription.DurationDays,
            IncludeFriday: subscription.IncludeFriday,
            StartDate: subscription.StartDate,
            EndDate: subscription.EndDate,
            AmountPaidKwd: subscription.PaidAmount is null || subscription.PaidAmount.Amount == 0m
                ? null
                : subscription.PaidAmount.Amount,
            CurrencyCode: subscription.PaidAmount is null || subscription.PaidAmount.Amount == 0m
                ? "KWD"
                : subscription.PaidAmount.CurrencyCode,
            PaymentVerifiedAtUtc: subscription.PaymentVerifiedAtUtc,
            Discount: discount,
            ReferralShare: referralShare));
    }

    private async Task<SubscriptionDiscountConfirmationDto?> ComposeDiscountAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        var offerRedemption = await _db.OfferRedemptions.AsNoTracking()
            .Include(x => x.Offer)
            .Where(x => x.SubscriptionId == subscription.Id)
            .OrderByDescending(x => x.RedeemedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (offerRedemption is not null)
        {
            var offerCode = offerRedemption.Offer?.PromoCode;
            return new SubscriptionDiscountConfirmationDto(
                PromoCodeId: offerRedemption.PromoCodeId ?? subscription.PromoCodeId,
                PromoCode: offerCode ?? subscription.PromoCode?.Code,
                OfferId: offerRedemption.OfferId,
                OfferCode: offerCode,
                Source: "Offer",
                DiscountKwd: offerRedemption.DiscountAppliedAmount.Amount);
        }

        if (subscription.PromoCodeId is not null)
        {
            var promoRedemption = await _db.PromoCodeRedemptions.AsNoTracking()
                .Where(x => x.SubscriptionId == subscription.Id && x.PromoCodeId == subscription.PromoCodeId
                    && x.Status != PromoCodeRedemptionStatus.Released)
                .OrderByDescending(x => x.HeldAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            var promoAmount = promoRedemption?.DiscountAppliedAmount.Amount
                ?? subscription.DiscountsAndReferralLogs
                    .Where(l => l.DiscountType == "PromoCode")
                    .OrderByDescending(l => l.AppliedAtUtc)
                    .Select(l => (decimal?)l.Amount.Amount)
                    .FirstOrDefault()
                ?? 0m;
            return new SubscriptionDiscountConfirmationDto(
                PromoCodeId: subscription.PromoCodeId,
                PromoCode: subscription.PromoCode?.Code,
                OfferId: null,
                OfferCode: null,
                Source: "PromoCode",
                DiscountKwd: promoAmount);
        }

        var referralLog = subscription.DiscountsAndReferralLogs
            .Where(l => l.DiscountType == "ReferralReward")
            .OrderByDescending(l => l.AppliedAtUtc)
            .FirstOrDefault();
        if (referralLog is not null)
        {
            return new SubscriptionDiscountConfirmationDto(
                PromoCodeId: null,
                PromoCode: referralLog.Code,
                OfferId: referralLog.Code is null ? null : await ResolveReferralCodeIdAsync(referralLog.Code, cancellationToken),
                OfferCode: null,
                Source: "Referral",
                DiscountKwd: referralLog.Amount.Amount);
        }

        return null;
    }

    private async Task<Guid?> ResolveReferralCodeIdAsync(string referralCode, CancellationToken cancellationToken)
        => await _db.ReferralCodes.AsNoTracking()
            .Where(r => r.Code == referralCode)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
