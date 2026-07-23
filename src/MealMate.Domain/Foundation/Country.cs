using MealMate.Domain.Common;

namespace MealMate.Domain.Foundation;

public sealed class Country : AuditableEntity
{
    public string Code { get; set; } = default!;

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public string CurrencyCode { get; set; } = "KWD";

    public bool IsActive { get; set; } = true;

    public ICollection<Region> Regions { get; set; } = new List<Region>();
}
