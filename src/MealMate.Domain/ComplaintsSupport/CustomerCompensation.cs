using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.ComplaintsSupport;

public sealed class CustomerCompensation : AuditableEntity
{
    public Guid ComplaintId { get; set; }

    public Complaint Complaint { get; set; } = default!;

    public Guid CustomerUserId { get; set; }

    public string CompensationType { get; set; } = default!;

    public Money? Amount { get; set; }

    public int? FreeDays { get; set; }

    public Money? PromoCreditAmount { get; set; }

    public string Status { get; set; } = default!;
}
