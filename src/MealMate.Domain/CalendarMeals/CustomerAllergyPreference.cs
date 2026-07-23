using MealMate.Domain.Common;

namespace MealMate.Domain.CalendarMeals;

public sealed class CustomerAllergyPreference : AuditableEntity
{
    public Guid CustomerUserId { get; set; }

    public string AllergenCode { get; set; } = default!;

    public string? AllergenNameAr { get; set; }

    public string? AllergenNameEn { get; set; }

    public string? Severity { get; set; }

    public bool IsDislike { get; set; }
}
