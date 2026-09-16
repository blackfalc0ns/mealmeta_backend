using MealMate.Domain.Common;

namespace MealMate.Domain.CollaborativeCampaigns;

public sealed class CampaignReservationAllocation : BaseEntity
{
    public Guid CheckoutSessionId { get; set; }

    public CampaignCheckoutSession CheckoutSession { get; set; } = default!;

    public Guid CampaignId { get; set; }

    public Guid BundleId { get; set; }

    public DateOnly ServiceDate { get; set; }

    public Guid RestaurantId { get; set; }

    public int Units { get; set; } = 1;
}
