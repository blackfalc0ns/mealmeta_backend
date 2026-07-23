using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.CollaborativeCampaigns;

public sealed class CampaignCapacityReview : AuditableEntity
{
    public Guid CampaignId { get; set; }

    public Campaign Campaign { get; set; } = default!;

    public Guid ReviewedByUserId { get; set; }

    public string? Notes { get; set; }

    public DateTime ReviewedAtUtc { get; set; }

    public VerificationStatus Decision { get; set; } = VerificationStatus.UnderReview;
}
