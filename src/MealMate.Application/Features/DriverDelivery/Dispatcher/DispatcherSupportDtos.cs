namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record SupportCountersDto(
    int ResolvedCount,
    int InResolutionCount,
    int OpenCount);

public sealed record SupportAreaChipDto(
    string AreaKey,
    string Name,
    int IssueCount,
    bool IsSelected);

public sealed record SupportIssueCardDto(
    Guid IssueId,
    string IssueCode,
    string Category,
    string BadgeText,
    string BadgeColor,
    string ReportedTimeText,
    string BoxCode,
    Guid DriverId,
    string DriverName,
    string? DriverAvatarUrl,
    string Area,
    string VehicleText,
    string Status);

public sealed record DispatcherSupportIssuesResponseDto(
    SupportCountersDto Counters,
    string SelectedArea,
    IReadOnlyList<SupportAreaChipDto> Areas,
    string SelectedStatus,
    IReadOnlyList<SupportIssueCardDto> Issues);

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
    string Message);
