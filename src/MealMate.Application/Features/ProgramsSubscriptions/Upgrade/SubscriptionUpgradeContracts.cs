using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions.Upgrade;

public sealed record CustomerUpgradeRequest(
    RestaurantTier TargetTier,
    string CallbackUrl,
    string ErrorUrl,
    int PaymentMethodId = 3,
    string? Reason = null,
    string? IdempotencyKey = null,
    int? ClientContractVersion = null);

public sealed record CustomerUpgradeOptionsDto(
    bool CanUpgrade,
    string? UnavailableReason,
    RestaurantTier CurrentTier,
    int RemainingServiceDays,
    int TotalServiceDays,
    IReadOnlyList<CustomerUpgradeOptionDto> Options,
    string UpgradeEndpoint,
    IReadOnlyList<CustomerPaymentMethodDto>? PaymentMethods = null);

public sealed record CustomerUpgradeTierInfoDto(
    RestaurantTier Tier,
    string TierNameAr,
    string TierNameEn,
    string? LogoUrl,
    int AccessibleRestaurantCount);

public sealed record CustomerUpgradeRestaurantDto(
    Guid Id,
    string NameAr,
    string NameEn,
    string? LogoUrl,
    decimal? Rating,
    RestaurantTier Tier);

public sealed record RestaurantRepetitionSummaryDto(
    int Limit,
    int RestaurantCount,
    int DurationDays);

public sealed record CustomerPaymentMethodDto(
    int? PaymentMethodId,
    string Code,
    string NameAr,
    string NameEn,
    string IntegrationType,
    decimal? ServiceCharge,
    decimal? TotalAmount,
    string CurrencyCode,
    bool IsAvailable,
    string? UnavailableReason,
    string? ImageUrl = null);

public sealed record CustomerUpgradeSummaryDto(
    Guid SubscriptionId,
    CustomerUpgradeTierInfoDto CurrentTier,
    CustomerUpgradeTierInfoDto TargetTier,
    IReadOnlyList<CustomerUpgradeRestaurantDto> CurrentTierRestaurants,
    IReadOnlyList<CustomerUpgradeRestaurantDto> TargetTierRestaurants,
    IReadOnlyList<CustomerUpgradeRestaurantDto> NewlyAddedRestaurants,
    DateOnly EffectiveFromDate,
    string EffectiveNoticeAr,
    string EffectiveNoticeEn,
    RestaurantRepetitionSummaryDto RestaurantRepetition,
    decimal CurrentRemainingCredit,
    decimal TargetRemainingPrice,
    decimal PriceDifference,
    string CurrencyCode,
    int RemainingServiceDays,
    int TotalServiceDays,
    IReadOnlyList<CustomerPaymentMethodDto> PaymentMethods,
    string UpgradeEndpoint);

public sealed record CustomerUpgradeOptionDto(
    RestaurantTier TargetTier,
    string TierNameAr,
    string TierNameEn,
    string? LogoUrl,
    bool IsCurrent,
    bool Selectable,
    int AccessibleRestaurantCount,
    decimal CurrentRemainingCredit,
    decimal TargetRemainingPrice,
    decimal GatewayRecovery,
    decimal PriceDifference,
    string CurrencyCode);

public sealed record CustomerUpgradeStartedDto(
    Guid UpgradeId,
    Guid SubscriptionId,
    RestaurantTier FromTier,
    RestaurantTier ToTier,
    decimal Amount,
    string CurrencyCode,
    string? PaymentUrl,
    bool IsPaid,
    VerificationStatus Status,
    int RemainingServiceDays);

public sealed record CustomerUpgradeStatusDto(
    Guid UpgradeId,
    Guid SubscriptionId,
    RestaurantTier FromTier,
    RestaurantTier ToTier,
    decimal Amount,
    string CurrencyCode,
    VerificationStatus Status,
    bool IsPaid,
    bool IsFailed,
    DateTime? EffectiveAtUtc,
    string? PaymentUrl);

public sealed record CustomerPendingUpgradeDto(
    Guid RequestId,
    RestaurantTier FromTier,
    RestaurantTier ToTier,
    decimal PriceDifference,
    VerificationStatus Status);
