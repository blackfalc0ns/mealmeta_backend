using MealMate.Domain.Common;

namespace MealMate.Domain.CollaborativeCampaigns;

public sealed class CampaignRestaurantCapacity : AuditableEntity
{
    public Guid CampaignId { get; set; }

    public Campaign Campaign { get; set; } = default!;

    public Guid RestaurantId { get; set; }

    public DateOnly? ServiceDate { get; set; }

    public int MaxBoxes { get; set; }

    public int ReservedBoxes { get; set; }
}
