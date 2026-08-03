using MealMate.Domain.Common;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class PackageContentVersion : AuditableEntity
{
    public Guid ProgramId { get; set; }

    public Program Program { get; set; } = default!;

    public Guid? BundleId { get; set; }

    public Bundle? Bundle { get; set; }

    public int VersionNumber { get; set; }

    public string ContentSnapshotJson { get; set; } = default!;

    public DateTime EffectiveFromUtc { get; set; }

    public DateTime? EffectiveToUtc { get; set; }

    public bool IsActive { get; set; } = true;

    public Guid? CreatedByAdminUserId { get; set; }

    public string? ChangeSummary { get; set; }
}
