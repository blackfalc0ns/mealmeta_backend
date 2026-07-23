using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.ComplaintsSupport;

public sealed class ResponsibilityDecision : AuditableEntity
{
    public Guid ComplaintId { get; set; }

    public Complaint Complaint { get; set; } = default!;

    public ResponsibilityParty Party { get; set; } = ResponsibilityParty.Undetermined;

    public string Reason { get; set; } = default!;

    public Guid DecidedByUserId { get; set; }

    public DateTime DecidedAtUtc { get; set; }
}
