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

    /// <summary>Legal entity this rule governs (Guid.Empty for pre-governance legacy rows).</summary>
    public Guid LegalEntityId { get; set; } = Guid.Empty;

    /// <summary>Server-normalized scope identity ("global" or the N-format scope entity id).</summary>
    public string NormalizedScopeId { get; set; } = RuleVersionScope.GlobalScopeId;

    /// <summary>Payload schema version interpreted by typed readers.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>SHA-256 hex hash of PayloadJson (pinned when the rule is submitted).</summary>
    public string? PayloadHash { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }

    public string? SubmittedPayloadHash { get; set; }

    /// <summary>Fingerprint of every governed field pinned when the version is submitted.</summary>
    public string? SubmittedContentHash { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public DateTime? RejectedAtUtc { get; set; }

    public Guid? CheckerUserId { get; set; }

    public int RowVersion { get; set; }
}
