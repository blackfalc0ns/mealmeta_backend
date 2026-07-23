using MealMate.Domain.Common;

namespace MealMate.Domain.CollaborativeCampaigns;

public sealed class CampaignSubscriberCap : AuditableEntity
{
    public Guid CampaignId { get; set; }

    public Campaign Campaign { get; set; } = default!;

    public int Cap { get; set; }

    public int CurrentCount { get; set; }

    public DateTime? LastReviewedAtUtc { get; set; }
}
