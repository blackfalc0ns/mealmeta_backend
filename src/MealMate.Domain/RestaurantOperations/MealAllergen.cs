using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class MealAllergen : AuditableEntity
{
    public Guid MealId { get; set; }

    public Meal Meal { get; set; } = default!;

    public string AllergenCode { get; set; } = default!;

    public string? AllergenNameAr { get; set; }

    public string? AllergenNameEn { get; set; }
}
