using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class Meal : AuditableEntity
{
    public Guid MenuId { get; set; }

    public Menu Menu { get; set; } = default!;

    public Guid RestaurantId { get; set; }

    public string Code { get; set; } = default!;

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public string? DescriptionAr { get; set; }

    public string? DescriptionEn { get; set; }

    public bool IsAvailableForNewOrders { get; set; } = true;

    public DateTime? DeletedForNewOrdersAtUtc { get; set; }

    public decimal? Calories { get; set; }

    public decimal? ProteinG { get; set; }

    public decimal? CarbsG { get; set; }

    public decimal? FatG { get; set; }

    public ICollection<MealAllergen> Allergens { get; set; } = new List<MealAllergen>();

    public ICollection<MealRating> Ratings { get; set; } = new List<MealRating>();
}
