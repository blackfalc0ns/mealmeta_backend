using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.ComplaintsSupport;

public sealed class RestaurantBoxDeduction : AuditableEntity
{
    public Guid ComplaintId { get; set; }

    public Complaint Complaint { get; set; } = default!;

    public Guid RestaurantId { get; set; }

    public int BoxesDeducted { get; set; }

    public Money Amount { get; set; } = default!;

    public string Status { get; set; } = default!;
}
