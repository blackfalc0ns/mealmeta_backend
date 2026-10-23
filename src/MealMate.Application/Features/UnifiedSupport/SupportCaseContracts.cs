using MealMate.Domain.ComplaintsSupport;

namespace MealMate.Application.Features.UnifiedSupport;

public sealed record CreateSupportCaseRequest(
    SupportCaseChannel Channel,
    SupportCaseKind Kind,
    string? Category,
    string Subject,
    string Description,
    string IdempotencyKey,
    string? RelatedResourceType = null,
    Guid? RelatedResourceId = null);

public sealed record AddSupportMessageRequest(string Body, uint ExpectedVersion, string IdempotencyKey, Guid? TaskId = null, IReadOnlyList<string>? AttachmentKeys = null);

public sealed record AddSupportInternalNoteRequest(string Body, uint ExpectedVersion, string IdempotencyKey, IReadOnlyList<string>? AttachmentKeys = null);

public sealed record SupportCaseActionRequest(
    string? Action,
    uint ExpectedVersion,
    string IdempotencyKey,
    string? Reason = null,
    SupportCaseTaskType? TaskType = null,
    SupportCaseTaskRecipientType? RecipientType = null,
    Guid? RestaurantId = null,
    Guid? RecipientUserId = null,
    string? TaskSubject = null,
    string? TaskQuestion = null,
    DateTime? DueAtUtc = null,
    Guid? TaskId = null,
    string? TaskResult = null);

public sealed record SupportCaseTransferRequest(Guid TargetUserId, string Reason, uint ExpectedVersion, string IdempotencyKey);
public sealed record SupportCaseTransferDecisionRequest(uint ExpectedVersion, string IdempotencyKey, string? Reason = null);
public sealed record ReassignSupportTaskRequest(Guid RecipientUserId, string Reason, uint ExpectedVersion, string IdempotencyKey);

public sealed record AddSupportCaseLinkRequest(
    SupportCaseLinkRelation Relation,
    string ResourceType,
    Guid ResourceId,
    uint ExpectedVersion,
    string IdempotencyKey);

public sealed record SupportCaseListQuery(
    int PageNumber = 1,
    int PageSize = 20,
    SupportCaseStatus? Status = null,
    SupportCaseKind? Kind = null,
    string? Category = null);

public sealed record SupportCaseMessageDto(
    Guid Id,
    Guid AuthorUserId,
    string Visibility,
    string Body,
    DateTime CreatedAtUtc,
    Guid? TaskId,
    IReadOnlyList<SupportCaseAttachmentDto> Attachments);

public sealed record SupportCaseAttachmentDto(string FileName, string ContentType, long SizeBytes, string ReadPath);

public sealed record SupportCaseAttachmentFile(byte[] Bytes, string FileName, string ContentType);
public sealed record SupportCaseTaskRecipientDto(Guid UserId, string? DisplayName);
public sealed record SupportCaseAssignmentRecipientDto(Guid UserId, string? DisplayName);

public sealed record SupportCaseTaskDto(
    Guid Id,
    string Type,
    string Status,
    string? Subject,
    string Question,
    string? Result,
    Guid? RestaurantId,
    Guid? RecipientUserId,
    DateTime CreatedAtUtc,
    DateTime? DueAtUtc,
    DateTime? CompletedAtUtc);

public sealed record SupportCaseLinkDto(
    string Relation,
    string ResourceType,
    Guid ResourceId,
    DateTime CreatedAtUtc);

public sealed record SupportCaseDto(
    Guid Id,
    string Channel,
    string Kind,
    string Status,
    string OwnerTeam,
    string? Priority,
    string? Category,
    string? Subject,
    string? Description,
    Guid? RequesterUserId,
    Guid? RestaurantId,
    Guid? AssignedToUserId,
    string? TriageStatus,
    Guid? TriageCountryId,
    Guid? TriageRegionId,
    Guid? PendingTransferToUserId,
    string? PendingTransferReason,
    uint Version,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc,
    IReadOnlyList<SupportCaseMessageDto> Messages,
    IReadOnlyList<SupportCaseTaskDto> Tasks,
    IReadOnlyList<SupportCaseLinkDto> Links,
    string? RelatedResourceType,
    Guid? RelatedResourceId,
    bool CanTakeOverAssignment = false,
    MealMate.Application.Features.UnifiedSupport.Timing.SupportCaseTimingCycleDto? ActiveTimingCycle = null);

public sealed record SupportCasePageDto(
    IReadOnlyList<SupportCaseDto> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
