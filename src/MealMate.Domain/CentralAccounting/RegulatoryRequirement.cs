using MealMate.Domain.Common;

namespace MealMate.Domain.CentralAccounting;

public sealed class RegulatoryRequirement : AuditableEntity
{
    public string Authority { get; set; } = default!;

    public string RequirementCode { get; set; } = default!;

    public string SourceUrl { get; set; } = default!;

    public DateTime CapturedAtUtc { get; set; }

    /// <summary>User that verified the captured source; null means captured but unverified.</summary>
    public Guid? VerifiedByUserId { get; set; }

    public string EvidenceHash { get; set; } = default!;

    public DateTime EffectiveFromUtc { get; set; }

    public DateTime? EffectiveToUtc { get; set; }
}
