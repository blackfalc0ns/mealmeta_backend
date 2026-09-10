using MealMate.Application.Features.ProgramsSubscriptions.Freeze;
using MealMate.Application.Features.ProgramsSubscriptions.Upgrade;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions.CustomerSubscriptions;

public sealed record CustomerSubscriptionManagementScreenDto(
    CustomerManagedSubscriptionDto? CurrentSubscription,
    IReadOnlyList<CustomerManagedSubscriptionDto> Subscriptions,
    IReadOnlyList<CustomerSubscriptionInvoiceDto> Invoices,
    string? EmptyStateCode);

public sealed record CustomerCurrentSubscriptionScreenDto(
    CustomerSubscriptionSlimDto? Subscription,
    string? EmptyStateCode);

public sealed record CustomerManagedSubscriptionDto(
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
    string? ImageUrl,
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly SuggestedRenewalStartDate,
    int TotalServiceDays,
    int CompletedServiceDays,
    int RemainingServiceDays,
    decimal ProgressPercentage,
    decimal PaidAmount,
    string CurrencyCode,
    CustomerFrozenRestaurantDto? Restaurant,
    CustomerPendingFreezeDto? PendingFreeze,
    CustomerPendingUpgradeDto? PendingUpgrade,
    CustomerSubscriptionActionsDto Actions,
    IReadOnlyList<CustomerSubscriptionInvoiceDto> Invoices,
    CustomerActiveFreezeDto? ActiveFreeze = null,
    string Kind = "INDIVIDUAL",
    Guid? FamilyGroupId = null,
    int MemberCount = 1,
    string? FamilyName = null);

public sealed record CustomerSubscriptionSlimDto(
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
    string? ImageUrl,
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly NextRenewalDate,
    int TotalServiceDays,
    int CompletedServiceDays,
    int RemainingServiceDays,
    decimal ProgressPercentage,
    decimal PaidAmount,
    string CurrencyCode,
    CustomerFrozenRestaurantDto? Restaurant,
    CustomerPendingFreezeDto? PendingFreeze,
    CustomerPendingUpgradeDto? PendingUpgrade,
    CustomerSubscriptionActionsDto Actions,
    CustomerActiveFreezeDto? ActiveFreeze = null,
    string Kind = "INDIVIDUAL",
    Guid? FamilyGroupId = null,
    int MemberCount = 1,
    string? FamilyName = null);

public sealed record CustomerFrozenRestaurantDto(
    Guid RestaurantId,
    string NameAr,
    string NameEn,
    string? LogoUrl);

public sealed record CustomerSubscriptionActionsDto(
    bool CanRenew,
    bool CanFreeze,
    bool CanCancel,
    bool CanUpgrade,
    string? RenewUnavailableReason,
    string? FreezeUnavailableReason,
    string? CancelUnavailableReason,
    string? UpgradeUnavailableReason,
    int FreezeStartDelayDays,
    int MaximumFreezeDays,
    bool FreezeExtendsSubscriptionEnd,
    DateOnly EarliestFreezeStartDate,
    string RenewalEndpoint,
    string FreezeEndpoint,
    string CancellationEndpoint,
    string UpgradeEndpoint,
    string? CancellationQuoteEndpoint = null,
    string? FreezeNoticeAr = null,
    string? FreezeNoticeEn = null,
    string? FreezeNotice = null,
    string? FreezeDetailsEndpoint = null);

public sealed record CustomerSubscriptionInvoiceDto(
    Guid ReceiptId,
    string? InvoiceNumber,
    string? PaymentId,
    DateTime IssuedAtUtc,
    decimal Amount,
    string CurrencyCode,
    string Status,
    string Gateway,
    string? MaskedCardNumber,
    bool CanDownload,
    string? DownloadUnavailableReason);

public sealed record CustomerSubscriptionInvoiceListDto(
    Guid ReceiptId,
    string? InvoiceNumber,
    string? PaymentId,
    DateTime IssuedAtUtc,
    decimal Amount,
    string CurrencyCode,
    string Status,
    string StatusNameAr,
    string Gateway,
    string? MaskedCardNumber,
    bool CanDownload,
    string? DownloadUnavailableReason);

public sealed record CustomerDefaultPaymentMethodDto(
    bool HasSavedMethod,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? Gateway,
    string EmptyStateCode);

public sealed record CustomerUncompletedSubscriptionDto(
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
    string? ImageUrl,
    DateOnly StartDate,
    DateOnly EndDate,
    int DurationDays,
    bool IncludeFriday,
    decimal Amount,
    string CurrencyCode,
    string? DeliveryAddress,
    DateTime CreatedAtUtc,
    bool IsHoldActive,
    int? HoldRemainingSeconds,
    DateTime? HoldExpiresAtUtc,
    CustomerUncompletedActionsDto Actions,
    string Kind = "INDIVIDUAL",
    Guid? FamilyGroupId = null);

public sealed record CustomerUncompletedActionsDto(
    bool CanDelete,
    bool CanResume,
    string DeleteEndpoint,
    string ResumeEndpoint,
    string NextScreen,
    string PayEndpoint);
