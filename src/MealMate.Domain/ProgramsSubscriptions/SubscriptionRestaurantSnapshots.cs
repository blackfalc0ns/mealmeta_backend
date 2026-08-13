using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class SubscriptionRestaurantPoolSnapshot : AuditableEntity
{
    public Guid SubscriptionId { get; set; }
    public DateTime EligibilityCheckedAtUtc { get; set; }
    public int RestaurantCount { get; set; }
    public bool IsBackfilled { get; set; }
}

public sealed class SubscriptionRestaurantSnapshot : AuditableEntity
{
    public Guid PoolSnapshotId { get; set; }
    public Guid SubscriptionId { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid ProgramId { get; set; }
    public Guid BundleId { get; set; }
    public Guid RegionId { get; set; }
    public RestaurantTier Tier { get; set; }
    public string NameAr { get; set; } = default!;
    public string NameEn { get; set; } = default!;
    public bool IsInitiallySelected { get; set; }
    public string OfferingSnapshotJson { get; set; } = "{}";
    public DateTime CapturedAtUtc { get; set; }
}

public sealed class SubscriptionRestaurantDaySnapshot : AuditableEntity
{
    public Guid RestaurantSnapshotId { get; set; }
    public Guid SubscriptionId { get; set; }
    public Guid RestaurantId { get; set; }
    public DateOnly ServiceDate { get; set; }
    public Guid MenuPlanVersionId { get; set; }
    public string SlotRequirementsJson { get; set; } = "[]";
    public string MealsJson { get; set; } = "[]";
    public string ContentHash { get; set; } = default!;
    public DateTime CapturedAtUtc { get; set; }
}

public sealed class SubscriptionSnapshotMigrationIssue : AuditableEntity
{
    public Guid SubscriptionId { get; set; }
    public string ReasonCode { get; set; } = default!;
    public string DetailsJson { get; set; } = "{}";
    public DateTime RecordedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}
