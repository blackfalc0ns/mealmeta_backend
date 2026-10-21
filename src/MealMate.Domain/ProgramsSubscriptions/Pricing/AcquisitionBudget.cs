using MealMate.Domain.Common;

namespace MealMate.Domain.ProgramsSubscriptions.Pricing;

/// <summary>Approved campaign acquisition-loss ceiling. Amounts are KWD and never implicit.</summary>
public sealed class AcquisitionBudget : AuditableEntity
{
    public Guid CampaignId { get; set; }
    public Guid BundleId { get; set; }
    public Guid LegalEntityId { get; set; }
    public Guid RuleVersionId { get; set; }
    public Guid MakerUserId { get; set; }
    public Guid? CheckerUserId { get; set; }
    public string ApprovalReason { get; set; } = string.Empty;
    public decimal AuthorizedLimitKwd { get; set; }
    public int MaximumSales { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public bool IsActive { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public int RowVersion { get; set; }
}
