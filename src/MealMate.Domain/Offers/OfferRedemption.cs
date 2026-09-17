using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.Offers;

public sealed class OfferRedemption : BaseEntity
{
    public Guid OfferId { get; set; }
    public Offer Offer { get; set; } = default!;

    public Guid CustomerId { get; set; }
    public Guid SubscriptionId { get; set; }
    public Guid? PromoCodeId { get; set; }

    public Money DiscountAppliedAmount { get; set; } = default!;
    public Money PlatformFundedAmount { get; set; } = default!;
    public Money RestaurantFundedAmount { get; set; } = default!;

    public DateTime RedeemedAtUtc { get; set; } = DateTime.UtcNow;
}
