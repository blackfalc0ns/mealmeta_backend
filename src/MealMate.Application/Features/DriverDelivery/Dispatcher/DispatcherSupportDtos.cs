namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record SupportCountersDto(
    int OpenCount,
    int InProgressCount,
    int ResolvedCount,
    int InResolutionCount = 0);

public sealed record SupportAreaChipDto(
    string AreaKey,
    string Name,
    int Count,
    bool IsSelected,
    string? AreaName = null,
    int? IssueCount = null);

public sealed record SupportPaginationDto(
    int PageNumber,
    int PageSize,
    int TotalItems,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);

public sealed record SupportIssueCardDto(
    Guid Id,
    Guid IssueId,
    string BoxCode,
    string IssueCode,
    string Title,
    string Category,
    string IssueCategory,
    string BadgeText,
    string BadgeColor,
    string IssueCategoryColor,
    string ReportedTimeText,
    string TimeAgo,
    DateTime CreatedAtUtc,
    Guid DriverId,
    string DriverCode,
    string DriverName,
    string? DriverAvatarUrl,
    string Area,
    string VehicleText,
    string VehicleInfo,
    string Priority,
    string PriorityLabel,
    string PriorityColor,
    string Status,
    string StatusLabel);

public sealed record DispatcherSupportIssuesResponseDto(
    SupportCountersDto Counters,
    IReadOnlyList<SupportAreaChipDto> AreaChips,
    IReadOnlyList<SupportIssueCardDto> Issues,
    SupportPaginationDto Pagination,
    string? SelectedArea = null,
    IReadOnlyList<SupportAreaChipDto>? Areas = null,
    string? SelectedStatus = null);

public sealed record SupportProblemMetadataDto(
    string BoxCode,
    string Area,
    int AffectedBoxesCount,
    string AffectedBoxesText,
    string Priority,
    string PriorityText,
    string PriorityColor);

public sealed record SupportProblemDriverDto(
    Guid DriverId,
    string DriverCode,
    string FullName,
    string? AvatarUrl,
    string StatusText,
    string StatusColor,
    string? PhoneNumber);

public sealed record SupportProblemTripInfoDto(
    string CustomerName,
    string MealsCountText,
    string ExpectedDeliveryTimeText,
    string PickupLocation,
    string DeliveryAddress);

public sealed record SupportProblemDetailsDto(
    Guid IssueId,
    string Title,
    string ReportedTimeText,
    SupportProblemMetadataDto Metadata,
    SupportProblemDriverDto Driver,
    string Description,
    IReadOnlyList<string> EvidencePhotos,
    SupportProblemTripInfoDto TripInfo,
    string Status);

public sealed record SupportIssueSummaryBannerDto(
    string Title,
    string ReportedTimeText,
    string BoxCode,
    string Area,
    string AffectedBoxesText,
    string PriorityText,
    string PriorityColor);

public sealed record CurrentDriverUnavailableDto(
    Guid DriverId,
    string DriverCode,
    string FullName,
    string? AvatarUrl,
    string StatusText);

public sealed record ReplacementDriverCandidateDto(
    Guid DriverId,
    string DriverCode,
    string FullName,
    string? AvatarUrl,
    string StatusText,
    string StatusColor,
    decimal Rating,
    int ActiveOrdersCount,
    decimal DistanceKm,
    string DistanceText,
    bool IsSameArea);

public sealed record DispatcherReplacementCandidatesResponseDto(
    Guid IssueId,
    SupportIssueSummaryBannerDto Issue,
    CurrentDriverUnavailableDto CurrentDriver,
    IReadOnlyList<ReplacementDriverCandidateDto> Candidates);

public sealed record ReassignReplacementDriverRequest(
    Guid ReplacementDriverId,
    string? Notes = null);

public sealed record ReassignReplacementDriverResponseDto(
    Guid IssueId,
    Guid ReplacementDriverId,
    string DriverName,
    DateTime ReassignedAtUtc,
    string Message);

public sealed record ResolveSupportIssueRequest(
    string? ResolutionNotes = null);

public sealed record ResolveSupportIssueResponseDto(
    Guid IssueId,
    DateTime ResolvedAtUtc,
    string Message,
    Guid? DriverId = null);
