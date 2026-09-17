using MealMate.Domain.Common;

namespace MealMate.Domain.Offers;

public sealed class OfferBundle : BaseEntity
{
    public Guid OfferId { get; set; }
    public Offer Offer { get; set; } = default!;
    public Guid BundleId { get; set; }
}
