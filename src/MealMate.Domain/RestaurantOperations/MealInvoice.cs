using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class MealInvoice : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public DateOnly ServiceDate { get; set; }

    public string InvoiceNumber { get; set; } = default!;

    public int TotalBoxes { get; set; }

    public string? SnapshotJson { get; set; }
}
