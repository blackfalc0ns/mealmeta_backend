using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.Offers;

/// <summary>
/// Exact package combination targeted by an admin-created offer. The offer owns the
/// discount rule; allowed durations are the only variable in this target.
/// </summary>
public sealed class OfferPackageTarget : BaseEntity
{
    public Guid OfferId { get; set; }
    public Offer Offer { get; set; } = default!;

    /// <summary>Supported checkout values are Individual and Family.</summary>
    public string SubscriptionType { get; set; } = "Individual";

    public Guid ProgramId { get; set; }
    public Guid BundleId { get; set; }
    public RestaurantTier Tier { get; set; }

    /// <summary>Durations that share the parent offer discount rule.</summary>
    public int[] AllowedDurationDays { get; set; } = [];
}
