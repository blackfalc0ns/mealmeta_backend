using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class RestaurantClassification : AuditableEntity
{
    public Guid ClassificationRunId { get; set; }

    public ClassificationRun ClassificationRun { get; set; } = default!;

    public Guid RestaurantId { get; set; }

    public Guid ProgramId { get; set; }

    public Program Program { get; set; } = default!;

    public Guid BundleId { get; set; }

    public Bundle Bundle { get; set; } = default!;

    public RestaurantTier Tier { get; set; }

    public decimal ReferenceDailyBox { get; set; }

    public bool IsOutlierFlagged { get; set; }

    public string? OutlierReason { get; set; }
}
