using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class SubscriptionFreezeRequest : AuditableEntity
{
    public Guid SubscriptionId { get; set; }

    public Subscription Subscription { get; set; } = default!;

    public DateOnly FreezeStartDate { get; set; }

    public DateOnly FreezeEndDate { get; set; }

    public int FreezeDays { get; set; }

    public string? Reason { get; set; }

    public VerificationStatus Status { get; set; } = VerificationStatus.Submitted;

    public Guid? ApprovedByUserId { get; set; }

    public DateTime? ProcessedAtUtc { get; set; }
}
