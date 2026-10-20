using MealMate.Domain.Enums;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;

namespace MealMate.Application.Features.Offers.DTOs;

/// <summary>
/// How the mobile client may act on an offer in customer lists. DirectPurchase: the id is a
/// real active offer row addressable by GET /offers/{offerId}/purchase-options. CouponOnly:
/// the offer is redeemed via its promo code in the subscription flow (or is a campaign
/// projection whose id is a campaign id, not an offer id).
/// </summary>
public static class PurchaseModes
{
    public const string DirectPurchase = "DirectPurchase";
    public const string CouponOnly = "CouponOnly";

    public static string Of(Offer offer) =>
        string.IsNullOrWhiteSpace(offer.PromoCode) ? DirectPurchase : CouponOnly;
}

public sealed record OfferPackageTargetDto(
    string SubscriptionType,
    Guid ProgramId,
    Guid BundleId,
    RestaurantTier Tier,
    IReadOnlyList<int> AllowedDurationDays);

public sealed record OfferPackageTargetInput(
    string SubscriptionType,
    Guid ProgramId,
    Guid BundleId,
    RestaurantTier Tier,
    IReadOnlyList<int> AllowedDurationDays);

public sealed record OfferDto(
    Guid Id,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? BannerImageUrl,
    OfferStatus Status,
    OfferType Type,
    DiscountType DiscountType,
    OfferFundingMode FundingMode,
    decimal? DiscountRate,
    decimal? DiscountAmountKwd,
    decimal? PlatformShareRate,
    decimal? RestaurantShareRate,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    decimal? MinOrderAmountKwd,
    int? MinSubscriptionDays,
    int? MaxSubscriptionDays,
    int? TotalUsageLimit,
    int CurrentUsageCount,
    int PerCustomerLimit,
    CustomerSegmentFilter CustomerSegment,
    Guid? CampaignId,
    bool AllowPromoCodeStacking = false,
    int? RemainingSpots = null,
    string? PromoCode = null,
    string? TermsAndConditionsAr = null,
    string? TermsAndConditionsEn = null,
    IReadOnlyList<Guid>? EligibleProgramIds = null,
    IReadOnlyList<Guid>? EligibleBundleIds = null,
    IReadOnlyList<Guid>? EligibleRestaurantIds = null,
    string SourceType = "Offer",
    IReadOnlyList<OfferPackageTargetDto>? PackageTargets = null,
    bool IsAvailable = true,
    string? AvailabilityReasonCode = null,
    IReadOnlyList<string>? IneligibilityReasonCodes = null,
    DateTime? EligibilityCheckedAtUtc = null,
    bool IsSaved = false,
    string PurchaseMode = PurchaseModes.CouponOnly);

public sealed record OfferRedemptionResultDto(
    Guid RedemptionId,
    Guid OfferId,
    Guid CustomerId,
    Guid SubscriptionId,
    decimal DiscountAppliedAmount,
    decimal PlatformFundedAmount,
    decimal RestaurantFundedAmount,
    DateTime RedeemedAtUtc
);
