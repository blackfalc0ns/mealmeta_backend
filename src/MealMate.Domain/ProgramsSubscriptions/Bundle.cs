using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class Bundle : AuditableEntity
{
    public Guid ProgramId { get; set; }

    public Program Program { get; set; } = default!;

    public string Code { get; set; } = default!;

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public int MealsPerDay { get; set; }

    public int BreakfastCount { get; set; }

    public int LunchCount { get; set; }

    public int DinnerCount { get; set; }

    public int SnackCount { get; set; }

    public string? Description { get; set; }

    public string? DescriptionAr { get; set; }

    public string? DescriptionEn { get; set; }

    /// <summary>Private storage key for the admin-uploaded bundle card image.</summary>
    public string? ImageStorageKey { get; set; }

    public ProgramStatus Status { get; set; } = ProgramStatus.Draft;

    public string? StatusReason { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>When true (and the parent program is family-available), this bundle appears in the family catalog.</summary>
    public bool AvailableForFamily { get; set; }

    public int SortOrder { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<PackageContentVersion> ContentVersions { get; set; } = new List<PackageContentVersion>();
}

