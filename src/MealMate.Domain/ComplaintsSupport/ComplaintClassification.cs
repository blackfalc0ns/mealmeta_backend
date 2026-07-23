using MealMate.Domain.Common;

namespace MealMate.Domain.ComplaintsSupport;

public sealed class ComplaintClassification : AuditableEntity
{
    public Guid ComplaintId { get; set; }

    public Complaint Complaint { get; set; } = default!;

    public string CategoryCode { get; set; } = default!;

    public string Severity { get; set; } = default!;

    public Guid? ClassifiedByUserId { get; set; }

    public DateTime ClassifiedAtUtc { get; set; }
}
