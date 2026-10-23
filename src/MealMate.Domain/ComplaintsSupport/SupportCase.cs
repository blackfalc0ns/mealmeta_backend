using MealMate.Domain.Common;

namespace MealMate.Domain.ComplaintsSupport;

public enum SupportCaseChannel { Customer, Driver, Restaurant, DeliveryManager }
public enum SupportCaseKind { Complaint, GeneralRequest, OperationalReport }
public enum SupportCaseStatus { New, Acknowledged, InProgress, WaitingOnRequester, WaitingOnParty, Resolved, Closed }
public enum SupportCaseOwnerTeam { PlatformSupport, RestaurantDeliveryManager }
public enum SupportCasePriority { P0, P1, P2, P3 }
public enum SupportCaseTriageStatus { KnownCountry, GlobalTriage }

public sealed class SupportCase : AuditableEntity
{
    private SupportCase() { }

    public SupportCaseChannel Channel { get; private set; }
    public SupportCaseKind Kind { get; private set; }
    public SupportCaseStatus Status { get; private set; } = SupportCaseStatus.New;
    public SupportCaseOwnerTeam OwnerTeam { get; private set; }
    public SupportCasePriority? Priority { get; private set; }
    public string Category { get; private set; } = default!;
    public string Subject { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public Guid RequesterUserId { get; private set; }
    public Guid? RestaurantId { get; private set; }
    public Guid? TriageCountryId { get; private set; }
    public Guid? TriageRegionId { get; private set; }
    public SupportCaseTriageStatus TriageStatus { get; private set; } = SupportCaseTriageStatus.GlobalTriage;
    public Guid? AssignedToUserId { get; private set; }
    public Guid? PendingTransferToUserId { get; private set; }
    public string? PendingTransferReason { get; private set; }
    public string? RelatedResourceType { get; private set; }
    public Guid? RelatedResourceId { get; private set; }
    public Guid? ActiveTimingCycleId { get; private set; }

    public void SetActiveTimingCycle(Guid cycleId)
    {
        ActiveTimingCycleId = cycleId;
    }

    public static SupportCase Create(
        SupportCaseChannel channel,
        SupportCaseKind kind,
        SupportCaseOwnerTeam ownerTeam,
        string category,
        string subject,
        string body,
        Guid requesterUserId,
        DateTime createdAtUtc,
        SupportCasePriority? priority = null,
        Guid? restaurantId = null,
        Guid? assignedToUserId = null,
        string? relatedResourceType = null,
        Guid? relatedResourceId = null,
        Guid? triageCountryId = null,
        Guid? triageRegionId = null)
    {
        if (!Enum.IsDefined(channel) || !Enum.IsDefined(kind) || !Enum.IsDefined(ownerTeam) || (priority.HasValue && !Enum.IsDefined(priority.Value)))
            throw new ArgumentOutOfRangeException(nameof(channel), "A supported case classification is required.");
        if (requesterUserId == Guid.Empty)
            throw new ArgumentException("A persisted requester is required.", nameof(requesterUserId));
        if (string.IsNullOrWhiteSpace(category) || category.Length > 100)
            throw new ArgumentException("Category must contain 1 to 100 characters.", nameof(category));
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 300)
            throw new ArgumentException("Subject must contain 1 to 300 characters.", nameof(subject));
        if (string.IsNullOrWhiteSpace(body) || body.Length > 4000)
            throw new ArgumentException("Body must contain 1 to 4000 characters.", nameof(body));
        if (createdAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Creation timestamp must be UTC.", nameof(createdAtUtc));

        if (restaurantId == Guid.Empty || assignedToUserId == Guid.Empty || triageCountryId == Guid.Empty || triageRegionId == Guid.Empty)
            throw new ArgumentException("Optional actor and restaurant IDs must be valid when present.");
        if (triageRegionId.HasValue && !triageCountryId.HasValue)
            throw new ArgumentException("A triage region requires its persisted country.");
        if ((relatedResourceType is null) != (relatedResourceId is null) || relatedResourceId == Guid.Empty)
            throw new ArgumentException("Related resource type and ID must be supplied together.");
        if (relatedResourceType is { Length: > 80 })
            throw new ArgumentException("Related resource type cannot exceed 80 characters.", nameof(relatedResourceType));

        return new SupportCase
        {
            Channel = channel,
            Kind = kind,
            OwnerTeam = ownerTeam,
            Category = category.Trim(),
            Subject = subject.Trim(),
            Body = body.Trim(),
            RequesterUserId = requesterUserId,
            CreatedAtUtc = createdAtUtc,
            CreatedByUserId = requesterUserId,
            Priority = priority,
            RestaurantId = restaurantId,
            TriageCountryId = triageCountryId,
            TriageRegionId = triageRegionId,
            TriageStatus = triageCountryId.HasValue ? SupportCaseTriageStatus.KnownCountry : SupportCaseTriageStatus.GlobalTriage,
            AssignedToUserId = assignedToUserId,
            RelatedResourceType = relatedResourceType,
            RelatedResourceId = relatedResourceId,
        };
    }

    public bool TryTransitionTo(SupportCaseStatus next)
    {
        var allowed = Status switch
        {
            SupportCaseStatus.New => next is SupportCaseStatus.Acknowledged or SupportCaseStatus.InProgress,
            SupportCaseStatus.Acknowledged => next is SupportCaseStatus.InProgress or SupportCaseStatus.WaitingOnRequester or SupportCaseStatus.WaitingOnParty,
            SupportCaseStatus.InProgress => next is SupportCaseStatus.WaitingOnRequester or SupportCaseStatus.WaitingOnParty or SupportCaseStatus.Resolved,
            SupportCaseStatus.WaitingOnRequester => next is SupportCaseStatus.InProgress,
            SupportCaseStatus.WaitingOnParty => next is SupportCaseStatus.InProgress or SupportCaseStatus.Resolved,
            SupportCaseStatus.Resolved => next is SupportCaseStatus.InProgress or SupportCaseStatus.Closed,
            SupportCaseStatus.Closed => next is SupportCaseStatus.InProgress,
            _ => false,
        };

        if (!allowed)
            return false;

        Status = next;
        return true;
    }

    public void ChangeOwner(SupportCaseOwnerTeam ownerTeam, Guid? assignedToUserId, Guid actorUserId, DateTime modifiedAtUtc)
    {
        if (!Enum.IsDefined(ownerTeam))
            throw new ArgumentOutOfRangeException(nameof(ownerTeam));
        Assign(assignedToUserId, actorUserId, modifiedAtUtc);
        OwnerTeam = ownerTeam;
    }

    public void Assign(Guid? assignedToUserId, Guid actorUserId, DateTime modifiedAtUtc)
    {
        if (actorUserId == Guid.Empty || modifiedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A persisted actor and UTC timestamp are required.");
        AssignedToUserId = assignedToUserId;
        ModifiedByUserId = actorUserId;
        ModifiedAtUtc = modifiedAtUtc;
    }

    public void TransferAssignment(Guid assignedToUserId, Guid actorUserId, DateTime modifiedAtUtc)
    {
        if (assignedToUserId == Guid.Empty || actorUserId == Guid.Empty || modifiedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Persisted actors and a UTC timestamp are required.");
        AssignedToUserId = assignedToUserId;
        ModifiedByUserId = actorUserId;
        ModifiedAtUtc = modifiedAtUtc;
        PendingTransferToUserId = null;
        PendingTransferReason = null;
    }

    public void RequestAssignmentTransfer(Guid targetUserId, string reason, Guid actorUserId, DateTime modifiedAtUtc)
    {
        if (targetUserId == Guid.Empty || actorUserId == Guid.Empty || string.IsNullOrWhiteSpace(reason)
            || reason.Trim().Length > 1000 || modifiedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A persisted target, actor, reason and UTC timestamp are required.");
        PendingTransferToUserId = targetUserId;
        PendingTransferReason = reason.Trim();
        ModifiedByUserId = actorUserId;
        ModifiedAtUtc = modifiedAtUtc;
    }

    public bool CancelAssignmentTransfer(Guid actorUserId, DateTime modifiedAtUtc)
    {
        if (!PendingTransferToUserId.HasValue || actorUserId == Guid.Empty || modifiedAtUtc.Kind != DateTimeKind.Utc) return false;
        PendingTransferToUserId = null;
        PendingTransferReason = null;
        ModifiedByUserId = actorUserId;
        ModifiedAtUtc = modifiedAtUtc;
        return true;
    }
}
