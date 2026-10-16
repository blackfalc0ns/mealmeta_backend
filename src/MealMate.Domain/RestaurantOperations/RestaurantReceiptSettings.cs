using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class RestaurantReceiptSettings : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public Guid? SelectedTemplateId { get; set; }

    public ReceiptTemplate? SelectedTemplate { get; set; }

    public Guid? AdminAssignedTemplateId { get; set; }

    public ReceiptTemplate? AdminAssignedTemplate { get; set; }

    public bool AllowRestaurantSelection { get; set; } = true;
}
