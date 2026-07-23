using MealMate.Domain.Common;

namespace MealMate.Domain.ComplaintsSupport;

public sealed class RestaurantComplaintResponse : AuditableEntity
{
    public Guid ComplaintId { get; set; }

    public Complaint Complaint { get; set; } = default!;

    public Guid RestaurantId { get; set; }

    public string ResponseText { get; set; } = default!;

    public Guid? RespondedByUserId { get; set; }

    public DateTime RespondedAtUtc { get; set; }
}
