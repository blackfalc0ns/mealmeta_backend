using MealMate.Domain.Common;

namespace MealMate.Domain.ComplaintsSupport;

public sealed class FreeDayCompensation : AuditableEntity
{
    public Guid ComplaintId { get; set; }

    public Complaint Complaint { get; set; } = default!;

    public Guid SubscriptionId { get; set; }

    public int DaysGranted { get; set; }

    public string Status { get; set; } = default!;
}
