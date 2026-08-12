using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class Ingredient : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public string Code { get; set; } = default!;

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public string? DescriptionAr { get; set; }

    public string? DescriptionEn { get; set; }

    public decimal? DefaultCaloriesPer100g { get; set; }

    public decimal? DefaultProteinPer100g { get; set; }

    public decimal? DefaultCarbsPer100g { get; set; }

    public decimal? DefaultFatPer100g { get; set; }

    /// <summary>Comma-separated EU14 allergen codes (e.g., "GLUTEN,MILK,TREE_NUTS").</summary>
    public string? AllergenCodes { get; set; }

    /// <summary>Ingredient category (e.g. poultry, seafood, dairy, produce, grains, other).</summary>
    public string? Category { get; set; }

    public decimal? DefaultWeightGrams { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<MealIngredient> MealIngredients { get; set; } = new List<MealIngredient>();
}
