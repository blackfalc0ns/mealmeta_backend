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

    /// <summary>ISO 3166-1 alpha-2/3 code supplied by the restaurant.</summary>
    public string? OriginCountryCode { get; set; }

    public string? SupplierName { get; set; }

    public bool IsLocal { get; set; }

    public bool IsHalalCertified { get; set; }

    public string? TraceabilityNote { get; set; }

    /// <summary>Allows operations to hide origin claims from customer-facing meal details.</summary>
    public bool IsOriginVisibleToCustomers { get; set; } = true;

    /// <summary>
    /// Core sources appear on the restaurant origin strip. Non-core ingredients appear on meals only.
    /// </summary>
    public bool IsCore { get; set; }

    /// <summary>Category-style icon shown on the restaurant origin strip. Required conceptually when <see cref="IsCore"/>.</summary>
    public string? RestaurantIconUrl { get; set; }

    /// <summary>Dish-style icon shown on meal ingredient chips. Used for every ingredient.</summary>
    public string? MealIconUrl { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<MealIngredient> MealIngredients { get; set; } = new List<MealIngredient>();
}
