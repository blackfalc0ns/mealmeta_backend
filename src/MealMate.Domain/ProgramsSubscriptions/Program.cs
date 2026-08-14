using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class Program : AuditableEntity
{
    public string Code { get; set; } = default!;

    /// <summary>Short onboarding/nutrition code (WL, LS, AT, MG, KT, LC).</summary>
    public string? ShortCode { get; set; }

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public string? DescriptionAr { get; set; }

    public string? DescriptionEn { get; set; }

    /// <summary>Short Arabic benefit labels displayed on the customer program card.</summary>
    public string[] KeywordsAr { get; set; } = [];

    /// <summary>Short English benefit labels displayed on the customer program card.</summary>
    public string[] KeywordsEn { get; set; } = [];

    /// <summary>Private storage key for the admin-uploaded program card image.</summary>
    public string? ImageStorageKey { get; set; }

    public ProgramStatus Status { get; set; } = ProgramStatus.Draft;

    public string? StatusReason { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public Guid? CurrentVersionId { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<Bundle> Bundles { get; set; } = new List<Bundle>();

    public ICollection<PackageContentVersion> ContentVersions { get; set; } = new List<PackageContentVersion>();
}

