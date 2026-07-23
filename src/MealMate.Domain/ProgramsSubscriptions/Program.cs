using MealMate.Domain.Common;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class Program : AuditableEntity
{
    public string Code { get; set; } = default!;

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public string? DescriptionAr { get; set; }

    public string? DescriptionEn { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public ICollection<Bundle> Bundles { get; set; } = new List<Bundle>();
}
