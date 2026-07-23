using MealMate.Domain.Common;

namespace MealMate.Domain.CollaborativeCampaigns;

public sealed class CampaignProgram : AuditableEntity
{
    public Guid CampaignId { get; set; }

    public Campaign Campaign { get; set; } = default!;

    public Guid ProgramId { get; set; }
}
