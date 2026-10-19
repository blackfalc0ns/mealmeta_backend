using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.Offers.DTOs;

public sealed record CustomerOfferPurchaseOptionsResponseDto(
    Guid OfferId,
    string SubscriptionType,
    DateTime ServerNowUtc,
    CustomerSubscriptionPrerequisitesDto Prerequisites,
    IReadOnlyList<MobileBlockingReasonDto> BlockingReasons,
    IReadOnlyList<OfferPurchaseOptionDto> Options,
    string? FamilyFlowDestination = null
);

public sealed record OfferPurchaseOptionDto(
    Guid ProgramId,
    string ProgramNameAr,
    string ProgramNameEn,
    Guid BundleId,
    string BundleNameAr,
    string BundleNameEn,
    string? ImageUrl,
    RestaurantTier Tier,
    string TierNameAr,
    string TierNameEn,
    int DurationDays,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IncludeFriday,
    int TotalServiceDays,
    int MealsPerDay,
    decimal? BasePrice,
    decimal? CampaignDiscount,
    decimal? PromoDiscount,
    decimal? WalletCredit,
    decimal? GatewayFee,
    decimal? FinalPrice,
    string CurrencyCode,
    bool CanCheckout,
    IReadOnlyList<MobileBlockingReasonDto> BlockingReasons,
    decimal? Discount = null
);
