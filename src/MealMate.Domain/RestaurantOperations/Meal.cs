using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.RestaurantOperations;

public sealed class Meal : AuditableEntity
{
    public Guid MenuId { get; set; }

    public Menu Menu { get; set; } = default!;

    public Guid RestaurantId { get; set; }

    public Guid? BundleId { get; set; }

    public string Code { get; set; } = default!;

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public string? DescriptionAr { get; set; }

    public string? DescriptionEn { get; set; }

    public string? Slot { get; set; }

    public bool IsAvailableForNewOrders { get; set; } = true;

    public DateTime? DeletedForNewOrdersAtUtc { get; set; }

    public VerificationStatus ApprovalStatus { get; set; } = VerificationStatus.Submitted;

    public string? RejectionReason { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public decimal? Calories { get; set; }

    public decimal? ProteinG { get; set; }

    public decimal? CarbsG { get; set; }

    public decimal? FatG { get; set; }

    public decimal? ServingWeightG { get; set; }
    public decimal? FiberG { get; set; }
    public decimal? NetCarbsG { get; set; }
    public decimal? SugarAlcoholG { get; set; }
    public string? SugarAlcoholType { get; set; }
    public decimal? SaturatedFatG { get; set; }
    public decimal? TotalSugarsG { get; set; }
    public decimal? AddedSugarsG { get; set; }
    public decimal? SodiumMg { get; set; }
    public string? NutritionSource { get; set; }
    public string? AnalysisReference { get; set; }
    public DateOnly? VerificationDate { get; set; }
    public string? VerifiedBy { get; set; }
    public string? RecipeVersion { get; set; }
    public string? RestaurantNotes { get; set; }
    public bool AllergensReviewed { get; set; }
    public string? PresentAllergenCodes { get; set; }
    public string? MayContainAllergenCodes { get; set; }
    public string? CrossContactNotes { get; set; }
    public bool DataResponsibilityConfirmed { get; set; }
    public string? NutritionTagCodes { get; set; }
    public string? SearchExclusionTagCodes { get; set; }

    public ICollection<MealAllergen> Allergens { get; set; } = new List<MealAllergen>();

    public ICollection<MealRating> Ratings { get; set; } = new List<MealRating>();

    public ICollection<MealIngredient> Ingredients { get; set; } = new List<MealIngredient>();

    public ICollection<MealImage> Images { get; set; } = new List<MealImage>();
}
