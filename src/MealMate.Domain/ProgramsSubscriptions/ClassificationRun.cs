using MealMate.Domain.Common;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class ClassificationRun : AuditableEntity
{
    public Guid ProgramId { get; set; }

    public Program Program { get; set; } = default!;

    public Guid BundleId { get; set; }

    public Bundle Bundle { get; set; } = default!;

    public decimal MeanReferenceDaily { get; set; }

    public decimal SdReferenceDaily { get; set; }

    public decimal LowerBound { get; set; }

    public decimal UpperBound { get; set; }

    public int RestaurantCount { get; set; }

    public string ConfidenceLevel { get; set; } = default!;

    public Guid? RuleVersionId { get; set; }

    public DateTime RunAtUtc { get; set; }

    public string? Notes { get; set; }

    public string? CohortHash { get; set; }

    public string? InputsJson { get; set; }

    public string AlgorithmVersion { get; set; } = "tier-mean-population-sd-half-v1";

    public string QuartileMethod { get; set; } = "NotUsed";

    public ICollection<RestaurantClassification> Classifications { get; set; } = new List<RestaurantClassification>();
}
