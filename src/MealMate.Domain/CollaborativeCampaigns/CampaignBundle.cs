using MealMate.Domain.Common;

namespace MealMate.Domain.CollaborativeCampaigns;

public sealed class CampaignBundle : AuditableEntity
{
    public Guid CampaignId { get; set; }

    public Campaign Campaign { get; set; } = default!;

    public Guid BundleId { get; set; }
}
