using MealMate.Domain.Common;

namespace MealMate.Domain.ComplaintsSupport;

public sealed class SupportTicket : AuditableEntity
{
    public Guid? CustomerUserId { get; set; }

    public string Subject { get; set; } = default!;

    public string Body { get; set; } = default!;

    public string Status { get; set; } = default!;

    public string Priority { get; set; } = default!;

    public Guid? AssignedToUserId { get; set; }
}
