using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class RestaurantMealLabelSettings : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public Guid? SelectedTemplateId { get; set; }

    public MealBoxLabelTemplate? SelectedTemplate { get; set; }

    public Guid? AdminAssignedTemplateId { get; set; }

    public MealBoxLabelTemplate? AdminAssignedTemplate { get; set; }

    public bool AllowRestaurantSelection { get; set; } = true;

    public int ConfiguredWidthMm { get; set; } = 70;

    public int ConfiguredHeightMm { get; set; } = 100;

    public string Orientation { get; set; } = "portrait"; // "portrait" | "landscape" | "square"

    public bool ShowPackagingDate { get; set; } = true;

    public bool ShowOrderReference { get; set; } = true;

    public bool ShowQuantity { get; set; } = true;
}
