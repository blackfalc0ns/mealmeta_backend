using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class MealInvoice : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public Guid? SubscriptionDayId { get; set; }

    public DateOnly ServiceDate { get; set; }

    public string InvoiceNumber { get; set; } = default!;

    public int TotalBoxes { get; set; }

    public string? SnapshotJson { get; set; }

    public Guid? TemplateId { get; set; }

    public int? TemplateVersion { get; set; }

    public string? BarcodeValue { get; set; }
}
