namespace MealMate.Application.Features.CollaborativeCampaigns.DTOs;

/// <summary>A typed checkout blocker surfaced to the customer app; Action names the screen that resolves it.</summary>
public sealed record CustomerCampaignBlock(string Code, string MessageAr, string MessageEn, string? Action);

/// <summary>
/// Client-safe campaign money projection. ReferencePrice and CustomerDiscountPercent are null
/// unless the approved pricing policy backs the reference-price claim; funding contributions
/// (restaurant/platform) are never projected to customers.
/// </summary>
public sealed record CustomerCampaignMoney(
    decimal? ReferencePrice,
    decimal DiscountAmount,
    decimal FinalPrice,
    string CurrencyCode,
    decimal? CustomerDiscountPercent,
    bool CanShowReferencePrice);

public sealed record CustomerCampaignSelection(
    Guid CampaignId,
    Guid ProgramId,
    Guid CampaignBundleId,
    int DurationDays,
    DateOnly StartDate,
    Guid AddressId,
    string? PromoCode);

/// <summary>
/// Server-side price offer for a selection. Holds no capacity: ExpiresAtUtc bounds the price
/// only; payment and subscription require a reserved checkout session.
/// </summary>
public sealed record CustomerCampaignQuote(
    Guid QuoteId,
    CustomerCampaignSelection Selection,
    CustomerCampaignMoney Price,
    DateTime ExpiresAtUtc,
    DateTime ServerNowUtc,
    bool CanCheckout,
    IReadOnlyList<CustomerCampaignBlock> BlockingReasons);
