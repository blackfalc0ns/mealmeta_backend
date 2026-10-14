using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions;

// Restaurant Bundle Pricing DTOs & Commands
public sealed record RestaurantBundlePricingDto(
    Guid Id,
    Guid RestaurantId,
    Guid ProgramId,
    Guid BundleId,
    decimal ReferencePrice26,
    decimal ContractedSupplyPrice26,
    PriceLockStatus LockStatus,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    string? ChangeRequestReason,
    Guid? PreviousPricingId,
    DateTime CreatedAtUtc);

public sealed record SetRestaurantPricingCommand(
    Guid RestaurantId,
    Guid ProgramId,
    Guid BundleId,
    decimal ReferencePrice26,
    decimal ContractedSupplyPrice26,
    string? ChangeRequestReason = null) : ICommand<RestaurantBundlePricingDto>;

public sealed record LockRestaurantPricingCommand(
    Guid Id,
    PriceLockStatus TargetStatus = PriceLockStatus.Locked) : ICommand<RestaurantBundlePricingDto>;

public sealed record GetRestaurantPricingQuery(
    Guid RestaurantId,
    Guid ProgramId,
    Guid BundleId) : IQuery<RestaurantBundlePricingDto>;

public sealed record GetRestaurantPricingsListQuery(
    Guid? RestaurantId = null,
    Guid? ProgramId = null,
    Guid? BundleId = null,
    PriceLockStatus? LockStatus = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<RestaurantBundlePricingDto>>;

/// <summary>MM-PRC-AMD-01: pending supply/reference change requests awaiting admin approval.</summary>
public sealed record RestaurantPricingChangeRequestDto(
    Guid Id,
    Guid RestaurantId,
    string RestaurantNameAr,
    string RestaurantNameEn,
    Guid ProgramId,
    string ProgramNameAr,
    string ProgramNameEn,
    Guid BundleId,
    string BundleNameAr,
    string BundleNameEn,
    decimal ReferencePrice26,
    decimal ContractedSupplyPrice26,
    decimal? PreviousReferencePrice26,
    decimal? PreviousContractedSupplyPrice26,
    PriceLockStatus LockStatus,
    string? ChangeRequestReason,
    DateTime EffectiveFromUtc,
    DateTime CreatedAtUtc);

public sealed record GetPendingRestaurantPricingChangesQuery
    : IQuery<IReadOnlyList<RestaurantPricingChangeRequestDto>>;

public sealed record SubscriptionBundleTierPricingDto(
    Guid ProgramId,
    string ProgramNameAr,
    string ProgramNameEn,
    Guid BundleId,
    string BundleNameAr,
    string BundleNameEn,
    RestaurantTier Tier,
    int RestaurantCount,
    decimal AveragePrice26DaysKwd,
    decimal AverageDailyPriceKwd,
    decimal CustomerPrice6DaysKwd,
    decimal CustomerPrice12DaysKwd,
    decimal CustomerPrice26DaysKwd,
    decimal Commission6Percent,
    decimal Commission12Percent,
    decimal Commission26Percent);

public sealed record GetSubscriptionBundleTierPricingsQuery
    : IQuery<IReadOnlyList<SubscriptionBundleTierPricingDto>>;

public sealed record CustomerSubscriptionDurationPriceDto(
    int DurationDays,
    decimal TotalPriceKwd,
    decimal DailyPriceKwd);

public sealed record CustomerSubscriptionCatalogItemDto(
    Guid ProgramId,
    string ProgramNameAr,
    string ProgramNameEn,
    Guid BundleId,
    string BundleNameAr,
    string BundleNameEn,
    RestaurantTier Tier,
    string TierNameAr,
    string TierNameEn,
    IReadOnlyList<CustomerSubscriptionDurationPriceDto> Prices,
    string CurrencyCode = "KWD");

public sealed record GetCustomerSubscriptionPricingCatalogQuery
    : IQuery<IReadOnlyList<CustomerSubscriptionCatalogItemDto>>;

// Classification DTOs & Commands
public sealed record RestaurantClassificationDto(
    Guid Id,
    Guid ClassificationRunId,
    Guid RestaurantId,
    Guid ProgramId,
    Guid BundleId,
    RestaurantTier Tier,
    decimal ReferenceDailyBox,
    bool IsOutlierFlagged,
    string? OutlierReason);

public sealed record ClassificationRunDto(
    Guid Id,
    Guid ProgramId,
    Guid BundleId,
    decimal MeanReferenceDaily,
    decimal SdReferenceDaily,
    decimal LowerBound,
    decimal UpperBound,
    string ConfidenceLevel,
    DateTime RunAtUtc,
    IReadOnlyList<RestaurantClassificationDto> Classifications);

public sealed record RunClassificationCommand(
    Guid ProgramId,
    Guid BundleId) : ICommand<ClassificationRunDto>;

public sealed record GetLatestClassificationRunQuery(
    Guid ProgramId,
    Guid BundleId) : IQuery<ClassificationRunDto>;

// Customer Pricing Preview Query
public sealed record PreviewCustomerPricingQuery(
    Guid ProgramId,
    Guid BundleId,
    RestaurantTier Tier,
    int DurationDays,
    decimal CampaignDiscountKwd = 0m,
    decimal PromoCodeDiscountKwd = 0m,
    bool IncludeFriday = false,
    int FridayDays = 0,
    Guid? OfferId = null,
    Guid? CustomerId = null,
    string? PromoCode = null,
    string? CustomerSubscriptionType = "Individual",
    Guid ExcludeSubscriptionId = default) : IQuery<Pricing.PricingEngineResult>;
