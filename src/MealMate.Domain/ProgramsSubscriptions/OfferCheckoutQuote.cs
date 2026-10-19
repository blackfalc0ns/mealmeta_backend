using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.ProgramsSubscriptions;

/// <summary>
/// A short-lived, owned price and selection quote for individual offer direct payment.
/// Binds the authenticated customer, offer, package, tier, duration, dates, Friday flag,
/// normalized coupon, price components, and delivery address snapshot.
/// </summary>
public sealed class OfferCheckoutQuote : BaseEntity
{
    public OfferCheckoutQuote() { }

    public OfferCheckoutQuote(Guid id)
    {
        Id = id;
    }

    public Guid CustomerId { get; set; }
    public Guid OfferId { get; set; }
    public Guid ProgramId { get; set; }
    public Guid BundleId { get; set; }
    public RestaurantTier Tier { get; set; }
    public int DurationDays { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IncludeFriday { get; set; }
    public int FridayDays { get; set; }
    public int TotalServiceDays { get; set; }
    public string? PromoCode { get; set; }

    public decimal BasePrice { get; set; }
    public decimal CampaignDiscount { get; set; }
    public decimal PromoDiscount { get; set; }
    public decimal WalletCredit { get; set; }
    public decimal GatewayFee { get; set; }
    public decimal FinalPrice { get; set; }
    public string CurrencyCode { get; set; } = "KWD";

    public Guid? AddressId { get; set; }
    public string? PayloadSnapshotHash { get; set; }

    public DateTime ExpiresAtUtc { get; set; }
    public bool IsUsed { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
