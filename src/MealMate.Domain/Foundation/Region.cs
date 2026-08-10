using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.Foundation;

public sealed class Region : AuditableEntity
{
    public Guid CountryId { get; set; }

    public Country Country { get; set; } = default!;

    public Guid? ParentRegionId { get; set; }

    public Region? ParentRegion { get; set; }

    public ICollection<Region> ChildRegions { get; set; } = new List<Region>();

    public RegionType Type { get; set; } = RegionType.Area;

    public string Code { get; set; } = default!;

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public bool IsActive { get; set; } = true;
}
