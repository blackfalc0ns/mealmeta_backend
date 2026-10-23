using MealMate.Domain.Common;

namespace MealMate.Domain.ComplaintsSupport;

public enum SupportCaseLinkRelation { OperationalSource, Incident, RelatedCase, CompensationReference }

public sealed class SupportCaseLink : BaseEntity
{
    public Guid SupportCaseId { get; set; }
    public SupportCaseLinkRelation Relation { get; set; }
    public string ResourceType { get; set; } = default!;
    public Guid ResourceId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
