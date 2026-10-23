using MealMate.Domain.Common;

namespace MealMate.Domain.ComplaintsSupport;

public enum SupportCaseTaskRecipientType { Requester, Restaurant, DeliveryManager, PlatformTeam }
public enum SupportCaseTaskType { RestaurantEvidence, DeliveryAction, PlatformReview }
public enum SupportCaseTaskStatus { Open, Completed, Cancelled, Expired }

public sealed class SupportCaseTask : BaseEntity
{
    public Guid SupportCaseId { get; set; }
    public SupportCaseTaskType Type { get; set; }
    public SupportCaseTaskRecipientType RecipientType { get; set; }
    public Guid? RecipientUserId { get; set; }
    public Guid? RestaurantId { get; set; }
    public SupportCaseTaskStatus Status { get; private set; } = SupportCaseTaskStatus.Open;
    public string Subject { get; set; } = default!;
    public string Question { get; set; } = default!;
    public string? Result { get; private set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DueAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public Guid? CompletedByUserId { get; private set; }

    public bool Complete(Guid actorUserId, string result, DateTime completedAtUtc)
    {
        if (Status != SupportCaseTaskStatus.Open || actorUserId == Guid.Empty || string.IsNullOrWhiteSpace(result) || result.Trim().Length > 4000 || completedAtUtc.Kind != DateTimeKind.Utc)
            return false;

        Status = SupportCaseTaskStatus.Completed;
        Result = result.Trim();
        CompletedAtUtc = completedAtUtc;
        CompletedByUserId = actorUserId;
        return true;
    }

    public bool Cancel(Guid actorUserId, string reason)
    {
        if (Status != SupportCaseTaskStatus.Open || actorUserId == Guid.Empty
            || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000) return false;
        Status = SupportCaseTaskStatus.Cancelled;
        return true;
    }
}
