using MealMate.Domain.ComplaintsSupport;

namespace MealMate.Application.Features.UnifiedSupport.Timing;

public sealed record CreateSupportTimingRuleDto(
    SupportCaseChannel? Channel,
    SupportCasePriority? Priority,
    string? Category,
    double FirstResponseMinutes,
    double ResolutionMinutes,
    double EscalationThresholdMinutes,
    bool RequiresImmediateAck,
    int EvaluationOrder);

public sealed record PublishSupportTimingPolicyRequest(
    string Name,
    string? Description,
    string PublicationReason,
    string IdempotencyKey,
    int ExpectedVersion,
    IReadOnlyList<CreateSupportTimingRuleDto> Rules);

public sealed record SupportTimingRuleDto(
    Guid Id,
    Guid PolicyId,
    string? Channel,
    string? Priority,
    string? Category,
    double FirstResponseMinutes,
    double ResolutionMinutes,
    double EscalationThresholdMinutes,
    bool RequiresImmediateAck,
    int EvaluationOrder);

public sealed record SupportTimingPolicyDto(
    Guid Id,
    int Version,
    string Name,
    string? Description,
    bool IsActive,
    DateTime PublishedAtUtc,
    Guid PublishedByUserId,
    string PublicationReason,
    IReadOnlyList<SupportTimingRuleDto> Rules);

public sealed record SupportCaseTimingCycleDto(
    Guid Id,
    Guid SupportCaseId,
    int CycleNumber,
    Guid? PolicyId,
    int? PolicyVersion,
    Guid? MatchedRuleId,
    DateTime StartedAtUtc,
    DateTime? FirstResponseDeadlineUtc,
    DateTime? ResolutionDeadlineUtc,
    DateTime? FirstResponseAtUtc,
    DateTime? ResolvedAtUtc,
    DateTime? ClosedAtUtc,
    bool IsFirstResponseBreached,
    bool IsResolutionBreached,
    bool IsEscalated,
    DateTime? EscalatedAtUtc,
    string? EscalationReason,
    bool IsPolicyUnconfigured,
    bool IsUnrouted,
    string? Notes,
    DateTime? EscalationDeadlineUtc = null);
