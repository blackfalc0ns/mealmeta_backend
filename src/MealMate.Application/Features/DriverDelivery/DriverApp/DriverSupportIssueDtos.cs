namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record DriverSupportIssuesResponseDto(int AllCount, int UnderReviewCount, int AwaitingReplyCount,
    int ResolvedCount, int TotalCount, int PageNumber, int PageSize, IReadOnlyList<DriverSupportIssueListItemDto> Issues);
public sealed record DriverSupportIssueListItemDto(Guid IssueId, string Category, string CategoryLabel, string Title,
    string Status, string StatusLabel, DateTime SubmittedAtUtc, string? OrderCode);
public sealed record DriverSupportIssueDetailDto(Guid IssueId, string Category, string CategoryLabel, string Title,
    string Description, string Status, string RawStatus, string StatusLabel, DateTime SubmittedAtUtc,
    DateTime? UpdatedAtUtc, string? OrderCode, string? Address, string? Area, DateTime? OrderCreatedAtUtc,
    IReadOnlyList<DriverSupportIssueStageDto> Timeline, IReadOnlyList<string> EvidenceUrls,
    string? ResolutionNotes, DateTime? ResolvedAtUtc, bool CanContactSupport, string? ContactChannel);
public sealed record DriverSupportIssueStageDto(string Stage, string Label, string State, DateTime? OccurredAtUtc);
public sealed record AddDriverSupportIssuePhotosRequest(IReadOnlyList<string> StorageKeys);
public sealed record DispatcherSupportProgressRequest(string Stage, string? Notes = null);
public sealed record DriverSupportIssueMutationDto(Guid IssueId, string Status, IReadOnlyList<string> EvidenceUrls);
