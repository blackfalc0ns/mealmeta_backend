using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;

namespace MealMate.Application.Features.CustomerCheckout;

public sealed record CheckoutCatalogItem(
    Guid ProgramId,
    string ProgramNameAr,
    string ProgramNameEn,
    Guid BundleId,
    string BundleNameAr,
    string BundleNameEn);

public sealed record CheckoutAvailabilityRequest(
    Guid ProgramId,
    Guid BundleId,
    RestaurantTier Tier,
    int DurationDays,
    DateOnly StartDate,
    Guid RegionId,
    bool IncludeFriday = false);

public sealed record StartCheckoutRequest(
    Guid ProgramId,
    Guid BundleId,
    RestaurantTier Tier,
    int DurationDays,
    string CallbackUrl,
    string ErrorUrl,
    DateOnly StartDate,
    bool IncludeFriday = false,
    string? CorrelationId = null,
    int PaymentMethodId = 3,
    string? PromoCode = null,
    string? IdempotencyKey = null,
    int? ClientContractVersion = null,
    Guid? OfferId = null);

public sealed record EligibleRestaurantDto(
    Guid RestaurantId,
    string NameAr,
    string NameEn,
    Guid MenuPlanVersionId,
    decimal FinalPrice,
    bool IncludeFriday,
    int FridayDays,
    int TotalServiceDays);

public sealed record CheckoutStartedDto(
    Guid SubscriptionId,
    string PaymentUrl,
    DateTime HoldExpiresAtUtc,
    Guid MenuPlanVersionId,
    decimal FinalPrice,
    Guid? PriceSnapshotId = null,
    DateTime? EligibilityCheckedAtUtc = null,
    IReadOnlyList<EligibleMenuDateDto>? MenuPlanVersionsByDate = null,
    Guid? RestaurantPoolSnapshotId = null,
    int RestaurantSnapshotCount = 0,
    string? PaymentStatus = null,
    string? FulfillmentStatus = null,
    bool IncludeFriday = false,
    int FridayDays = 0,
    int TotalServiceDays = 0,
    Guid? RestaurantId = null,
    Guid? RegionId = null,
    DateOnly? StartDate = null,
    string? DeliveryAddress = null,
    DateOnly? ChosenStartDate = null);

public sealed record SubscriptionPaymentStatusDto(
    Guid SubscriptionId,
    string PaymentStatus,
    bool IsPaid,
    bool IsFailed,
    decimal Amount,
    string CurrencyCode);

public sealed record SubscriptionRestaurantPoolItemDto(
    Guid RestaurantId,
    string NameAr,
    string NameEn,
    RestaurantTier Tier,
    bool IsInitiallySelected,
    DateTime CapturedAtUtc,
    string? LogoUrl = null,
    decimal? Rating = null,
    int RatingCount = 0,
    string? TierNameAr = null,
    string? TierNameEn = null,
    string? TierName = null,
    string? TierLogoUrl = null);

public sealed record SubscriptionRestaurantPoolDto(
    Guid RestaurantPoolSnapshotId,
    DateTime EligibilityCheckedAtUtc,
    int RestaurantCount,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasNextPage,
    bool HasPreviousPage,
    IReadOnlyList<SubscriptionRestaurantPoolItemDto> Restaurants);

public sealed record CheckoutReservationOutcome(
    SubscriptionDto? Subscription,
    RestaurantEligibilityDto? Restaurant,
    string? ErrorCode,
    DateTime CheckedAtUtc,
    DateTime ExpiresAtUtc,
    Guid? PoolSnapshotId = null,
    int RestaurantCount = 0,
    Guid RegionId = default);

public sealed class CheckoutReservationException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed record ResumeCheckoutRequest(
    DateOnly? StartDate = null,
    string? CallbackUrl = null,
    string? ErrorUrl = null,
    int PaymentMethodId = 3);

public sealed record CustomerSubscriptionResumePreviewDto(
    Guid SubscriptionId,
    string Status,
    string StatusNameAr,
    Guid ProgramId,
    string ProgramNameAr,
    string ProgramNameEn,
    Guid BundleId,
    string BundleNameAr,
    string BundleNameEn,
    RestaurantTier Tier,
    string TierNameAr,
    string TierNameEn,
    string? TierLogoUrl,
    int MealsPerDay,
    int DurationDays,
    bool IncludeFriday,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Amount,
    string CurrencyCode,
    string? DeliveryAddress,
    bool CanPayDirectly,
    string? Reason,
    int? HoldRemainingSeconds,
    DateTime? HoldExpiresAtUtc,
    DateOnly SuggestedStartDate,
    string ResumeEndpoint,
    string NextScreen,
    string PayEndpoint,
    DateOnly CheckoutStartDate);
