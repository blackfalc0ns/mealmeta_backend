using MealMate.Domain.Common;

namespace MealMate.Domain.Influencers;

public sealed class InfluencerEligibilityRule : AuditableEntity
{
    public Guid? InfluencerProfileId { get; set; }

    public Guid? RuleVersionId { get; set; }

    public string PayloadJson { get; set; } = default!;

    public bool IsActive { get; set; } = true;
}
