using MealMate.Domain.Common;

namespace MealMate.Domain.ComplaintsSupport;

public sealed class AdminComplaintReview : AuditableEntity
{
    public Guid ComplaintId { get; set; }

    public Complaint Complaint { get; set; } = default!;

    public Guid ReviewerUserId { get; set; }

    public string? DecisionNotes { get; set; }

    public DateTime ReviewedAtUtc { get; set; }

    public string Status { get; set; } = default!;
}
