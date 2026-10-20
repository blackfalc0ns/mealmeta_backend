using MealMate.Domain.Common;

namespace MealMate.Domain.CollaborativeCampaigns;

/// <summary>
/// A saved server-side price offer for one customer selection. Holds no capacity and no
/// payment: it records the exact evaluation inputs and computed price so a later checkout
/// session can be derived without re-trusting client values. ExpiresAtUtc bounds the price
/// only — the reservation hold has its own lifetime.
/// </summary>
public sealed class CustomerCampaignQuote : AuditableEntity
{
    public Guid CustomerId { get; set; }

    public Guid CampaignId { get; set; }

    public Guid ProgramId { get; set; }

    /// <summary>The campaign-bundle relationship id addressed by clients.</summary>
    public Guid CampaignBundleId { get; set; }

    /// <summary>The original catalog bundle behind the relationship.</summary>
    public Guid CatalogBundleId { get; set; }

    public int DurationDays { get; set; }

    public DateOnly StartDate { get; set; }

    public Guid AddressId { get; set; }

    public string? PromoCode { get; set; }

    public decimal ReferencePrice { get; set; }

    /// <summary>Headline campaign discount for the selection.</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>Coupon contribution; zero while stacking is not explicitly allowed.</summary>
    public decimal CouponDiscountAmount { get; set; }

    public decimal FinalPrice { get; set; }

    public string CurrencyCode { get; set; } = default!;

    public decimal? CustomerDiscountPercent { get; set; }

    public bool CanShowReferencePrice { get; set; }

    public Guid? PricingPolicyId { get; set; }

    public int PricingPolicyVersion { get; set; }

    public int CampaignSnapshotVersion { get; set; }

    /// <summary>Derived real service schedule for the selection; the client never supplies dates.</summary>
    public DateOnly[] ServiceDates { get; set; } = Array.Empty<DateOnly>();

    public DateTime ExpiresAtUtc { get; set; }
}
