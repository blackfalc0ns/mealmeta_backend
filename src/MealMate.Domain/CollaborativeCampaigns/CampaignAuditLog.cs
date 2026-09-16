using MealMate.Domain.Common;

namespace MealMate.Domain.CollaborativeCampaigns;

public sealed class CampaignAuditLog : BaseEntity
{
    public Guid CampaignId { get; set; }

    public Campaign Campaign { get; set; } = default!;

    public string ActorId { get; set; } = default!;

    public string Action { get; set; } = default!;

    public string? BeforeStateJson { get; set; }

    public string? AfterStateJson { get; set; }

    public string? DetailsJson { get; set; }

    public string? JustificationReason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
