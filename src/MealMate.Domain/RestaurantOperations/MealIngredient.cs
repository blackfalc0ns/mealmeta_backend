using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class MealIngredient : AuditableEntity
{
    public Guid MealId { get; set; }

    public Meal Meal { get; set; } = default!;

    public Guid IngredientId { get; set; }

    public Ingredient Ingredient { get; set; } = default!;

    public decimal PortionGrams { get; set; }

    public bool IsOptional { get; set; }

    public int SortOrder { get; set; }
}
