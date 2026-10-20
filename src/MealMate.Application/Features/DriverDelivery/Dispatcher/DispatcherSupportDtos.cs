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
    string? BoxCode,
    string? IssueCode,
    string Title,
    string Category,
    string IssueCategory,
    string BadgeText,
    string BadgeColor,
    string IssueCategoryColor,
    string ReportedTimeText,
    string TimeAgo,
    DateTime CreatedAtUtc,
    Guid? DriverId,
    string? DriverCode,
    string? DriverName,
    string? DriverAvatarUrl,
    string? Area,
    string? VehicleText,
    string? VehicleInfo,
    string? Priority,
    string PriorityLabel,
    string? PriorityColor,
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

public sealed record DriverVehicleDto(
    string? Type,
    string? Model,
    string? Color,
    string? PlateNumber);

public sealed record SupportEvidencePhotoDto(
    Guid Id,
    string? Url,
    string? ThumbnailUrl,
    DateTime UploadedAtUtc);

public sealed record SupportProblemResolutionDto(
    string Notes,
    DateTime? ResolvedAtUtc,
    Guid? ResolvedById,
    string? ResolvedByName);

public sealed record SupportProblemMetadataDto(
    string? BoxCode,
    string? Area,
    int? AffectedBoxesCount,
    string? AffectedBoxesText,
    string? Priority,
    string PriorityText,
    string? PriorityColor);

public sealed record SupportProblemDriverDto(
    Guid? DriverId,
    string? DriverCode,
    string? FullName,
    string? AvatarUrl,
    string Status,
    string StatusText,
    string StatusColor,
    string? PhoneNumber,
    string? VehicleInfo,
    DriverVehicleDto? Vehicle = null);

public sealed record SupportProblemTripInfoDto(
    string? CustomerName,
    int? MealsCount,
    string MealsCountText,
    DateTime? ExpectedDeliveryFromUtc,
    DateTime? ExpectedDeliveryToUtc,
    string? ExpectedDeliveryTimeText,
    string? PickupLocation,
    string? DeliveryAddress,
    string? MealSummary = null);

public sealed record SupportProblemDetailsDto(
    Guid IssueId,
    string Title,
    string Category,
    string CategoryLabel,
    string? CategoryColor,
    DateTime CreatedAtUtc,
    string ReportedTimeText,
    SupportProblemMetadataDto Metadata,
    SupportProblemDriverDto Driver,
    string Description,
    IReadOnlyList<SupportEvidencePhotoDto> EvidencePhotos,
    SupportProblemTripInfoDto TripInfo,
    string Status,
    string StatusLabel,
    SupportProblemResolutionDto? Resolution = null,
    string? VehicleInfo = null);

public sealed record SupportIssueSummaryBannerDto(
    Guid IssueId,
    string Title,
    string Category,
    string CategoryLabel,
    string CategoryColor,
    DateTime? CreatedAtUtc,
    string ReportedTimeText,
    string? BoxCode,
    string? Area,
    int? AffectedBoxesCount,
    string AffectedBoxesText,
    string? Priority,
    string PriorityText,
    string? PriorityColor,
    string Status);

public sealed record CurrentDriverUnavailableDto(
    Guid? DriverId,
    string? DriverCode,
    string? FullName,
    string? AvatarUrl,
    string Status,
    string StatusText,
    string StatusColor,
    string? UnavailabilityReason,
    string? UnavailabilityReasonText);

public sealed record ReplacementDriverCandidateDto(
    Guid DriverId,
    string? DriverCode,
    string? FullName,
    string? AvatarUrl,
    string Status,
    string StatusText,
    string StatusColor,
    decimal? Rating,
    int ActiveOrdersCount,
    string? Area,
    bool? IsSameArea,
    decimal? DistanceKm,
    string? DistanceText,
    int? EstimatedArrivalMinutes,
    string? EstimatedArrivalText,
    string? VehicleInfo,
    DateTime? LastLocationUpdatedAtUtc,
    int RecommendationRank);

public sealed record CandidatePaginationDto(
    int PageNumber,
    int PageSize,
    int TotalItems,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);

public sealed record DispatcherReplacementCandidatesResponseDto(
    SupportIssueSummaryBannerDto Summary,
    SupportIssueSummaryBannerDto Issue,
    CurrentDriverUnavailableDto? CurrentDriver,
    IReadOnlyList<ReplacementDriverCandidateDto> Candidates,
    CandidatePaginationDto Pagination);

public sealed record ReassignReplacementDriverRequest(
    Guid ReplacementDriverId,
    string? Notes = null);

public sealed record ReassignReplacementDriverResponseDto(
    bool Success,
    Guid IssueId,
    Guid PreviousDriverId,
    string IssueStatus,
    SupportProblemDriverDto ReplacementDriver,
    DateTime ReassignedAtUtc,
    string Message,
    Guid ReplacementDriverId,
    string? ReplacementDriverCode,
    string? DriverName,
    Guid? HandoverId = null,
    string? HandoverStatus = null);

public sealed record ResolveSupportIssueRequest(
    string? ResolutionNotes = null);

public sealed record ResolveSupportIssueResponseDto(
    Guid IssueId,
    string Status,
    string StatusLabel,
    SupportProblemResolutionDto Resolution,
    DateTime ResolvedAtUtc,
    string Message,
    Guid? DriverId = null);
