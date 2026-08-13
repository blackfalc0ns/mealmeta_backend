using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class AllergenDefinition : AuditableEntity
{
    public string Code { get; set; } = default!;
    public string NameAr { get; set; } = default!;
    public string NameEn { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class FoodPreferenceDefinition : AuditableEntity
{
    public string Code { get; set; } = default!;
    public string NameAr { get; set; } = default!;
    public string NameEn { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class IngredientFoodPreference : AuditableEntity
{
    public Guid IngredientId { get; set; }
    public string PreferenceCode { get; set; } = default!;
}

public sealed class MealFoodPreference : AuditableEntity
{
    public Guid MealId { get; set; }
    public string PreferenceCode { get; set; } = default!;
}
