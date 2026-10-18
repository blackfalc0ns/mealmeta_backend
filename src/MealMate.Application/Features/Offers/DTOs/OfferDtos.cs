using MealMate.Domain.Enums;
using MealMate.Domain.Offers.Enums;

namespace MealMate.Application.Features.Offers.DTOs;

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
    DateTime? EligibilityCheckedAtUtc = null
);

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
