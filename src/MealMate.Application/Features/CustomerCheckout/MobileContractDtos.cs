namespace MealMate.Application.Features.CustomerCheckout;

/// <summary>Stable additive metadata consumed by mobile clients for retry and refresh decisions.</summary>
public sealed record MobileRetryAdviceDto(
    bool Retryable,
    bool RefreshSummaryRequired = false,
    bool RefreshAvailabilityRequired = false,
    string? RetryAfterSeconds = null);

/// <summary>Localized, machine-readable reason shown by mobile without parsing server prose.</summary>
public sealed record MobileBlockingReasonDto(
    string Code,
    string MessageAr,
    string MessageEn,
    string? Field = null,
    MobileRetryAdviceDto? Advice = null);

public sealed record MobileEligibilityDto(
    bool IsEligible,
    IReadOnlyList<MobileBlockingReasonDto> Reasons);

public sealed record MobileAvailabilityDto(
    bool IsAvailable,
    IReadOnlyList<MobileBlockingReasonDto> Reasons,
    DateTime? CheckedAtUtc = null);

/// <summary>All monetary components use the contract currency and three KWD decimals.</summary>
public sealed record MobileMoneyBreakdownDto(
    decimal Subtotal,
    decimal Discount,
    decimal WalletCredit,
    decimal GatewayFee,
    decimal FinalPrice,
    string CurrencyCode = "KWD",
    decimal? BasePrice = null,
    decimal? CampaignDiscount = null,
    decimal? PromoDiscount = null)
{
    /// <summary>Compatibility mapping for clients that still use the legacy names.</summary>
    public decimal LegacyBasePrice => BasePrice ?? Subtotal;
    public decimal LegacyDiscount => CampaignDiscount.GetValueOrDefault() + PromoDiscount.GetValueOrDefault();
}
