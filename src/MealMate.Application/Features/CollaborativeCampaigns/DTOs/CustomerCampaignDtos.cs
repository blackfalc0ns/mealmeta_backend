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

/// <summary>
/// Customer-facing campaign card. MinPrice is present only when a confirmed, policy-backed
/// option exists (never a fabricated "from" price); funding contributions are never projected.
/// </summary>
public sealed record CustomerCampaignListItemDto(
    Guid CampaignId,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    decimal? MinPrice,
    string? CurrencyCode,
    Guid? MinPriceCampaignBundleId,
    int? MinPriceDurationDays,
    int? RemainingSubscriberSlots,
    bool FullSequenceAvailable);

public sealed record CustomerCampaignsPageDto(
    IReadOnlyList<CustomerCampaignListItemDto> Items,
    string? NextCursor,
    DateTime ServerNowUtc);

public sealed record CustomerCampaignOptionDto(
    Guid CampaignBundleId,
    Guid CatalogBundleId,
    Guid ProgramId,
    string ProgramNameAr,
    string ProgramNameEn,
    string BundleNameAr,
    string BundleNameEn,
    string? BundleDescriptionAr,
    string? BundleDescriptionEn,
    int DurationDays,
    decimal? ReferencePrice,
    decimal FinalPrice,
    string CurrencyCode,
    decimal? CustomerDiscountPercent,
    bool CanShowReferencePrice,
    bool FullSequenceAvailable);

public sealed record CustomerCampaignDetailsDto(
    Guid CampaignId,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    DateTime ServerNowUtc,
    bool CanCheckout,
    int? RemainingSubscriberSlots,
    IReadOnlyList<CustomerCampaignBlock> BlockingReasons,
    IReadOnlyList<CustomerCampaignOptionDto> Options);
