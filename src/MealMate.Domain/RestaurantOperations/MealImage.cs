using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class MealImage : AuditableEntity
{
    public Guid MealId { get; set; }

    public Meal Meal { get; set; } = default!;

    public string ImageUrl { get; set; } = default!;

    public int DisplayOrder { get; set; }

    public bool IsPrimary { get; set; }

    public string? AltTextAr { get; set; }

    public string? AltTextEn { get; set; }
}
