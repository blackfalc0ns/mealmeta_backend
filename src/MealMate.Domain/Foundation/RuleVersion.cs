using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.Foundation;

/// <summary>
/// Versioned commercial/operational rule (CR-26 / CR-28). Immutable once Active;
/// changes create a new version with EffectiveFrom.
/// </summary>
public sealed class RuleVersion : AuditableEntity
{
    public string RuleKey { get; set; } = default!;

    public string VersionCode { get; set; } = default!;

    public RuleScopeType ScopeType { get; set; } = RuleScopeType.Global;

    public Guid? ScopeEntityId { get; set; }

    public RuleApprovalStatus ApprovalStatus { get; set; } = RuleApprovalStatus.Draft;

    public DateTime EffectiveFromUtc { get; set; }

    public DateTime? EffectiveToUtc { get; set; }

    public Guid? OwnerUserId { get; set; }

    public string? Reason { get; set; }

    public Guid? PreviousVersionId { get; set; }

    public RuleVersion? PreviousVersion { get; set; }

    /// <summary>JSON payload of rule parameters (rates, limits, curve coeffs, etc.).</summary>
    public string PayloadJson { get; set; } = "{}";
}
