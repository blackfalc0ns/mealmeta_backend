using MealMate.Domain.Common;

namespace MealMate.Domain.Foundation;

public sealed class Region : AuditableEntity
{
    public Guid CountryId { get; set; }

    public Country Country { get; set; } = default!;

    public string Code { get; set; } = default!;

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public bool IsActive { get; set; } = true;
}
