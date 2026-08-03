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
    decimal PromoCodeDiscountKwd = 0m) : IQuery<Pricing.PricingEngineResult>;
