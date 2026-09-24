namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record DispatcherOrderQueueDto(
    RestaurantContextDto Restaurant,
    DispatcherQueueCountsDto Counts,
    IReadOnlyList<DispatcherQueueBoxItemDto> Boxes,
    int Page = 1,
    int PageSize = 25,
    int TotalBoxes = 0,
    int TotalPages = 1,
    bool HasNextPage = false);

public sealed record DispatcherQueueCountsDto(
    int TotalCount,
    int PendingCount,
    int AssignedCount,
    int InDeliveryCount,
    int IssuesCount);

public sealed record DispatcherQueueBoxItemDto(
    Guid BoxId,
    string BoxCode,
    string ZoneName,
    int MealCount,
    string MealCountLabel,
    string DeliveryTimeWindow,
    double DistanceKm,
    string DistanceText,
    string Status,
    string Priority,
    string PriorityBadgeText,
    DriverSuggestionDto? Suggestion,
    Guid? AssignedDriverId,
    string? AssignedDriverName);

public sealed record DriverSuggestionDto(
    Guid DriverId,
    string DriverName,
    string? AvatarUrl,
    string SuggestionType,
    string Label);

public sealed record CandidateDriverDto(
    Guid DriverId,
    string FullName,
    string? AvatarUrl,
    string? PlateNumber,
    string? Phone,
    double DistanceKm,
    string DistanceText,
    int ActiveOrdersCount,
    int CurrentLoadBoxes,
    string CurrentLoadLabel,
    decimal Rating,
    string DriverStatus,
    string DriverStatusText,
    string? StatusTag,
    string EstimatedFinishTimeText,
    int Rank,
    bool IsRecommended,
    string? RecommendationReason);

public sealed record BoxDetailsSummaryDto(
    Guid BoxId,
    string BoxCode,
    string ZoneName,
    string DeliveryTimeWindow,
    int MealCount,
    string MealCountLabel,
    double DistanceKm,
    string DistanceText,
    string Priority,
    string PriorityBadgeText,
    string Status,
    string StatusBadgeText);

public sealed record BoxAssignmentDetailsDto(
    BoxDetailsSummaryDto Box,
    CandidateDriverDto? BestSuggestion,
    IReadOnlyList<CandidateDriverDto> Candidates);

public sealed record BoxContentSummaryDto(
    Guid BoxId,
    string BoxCode,
    string CustomerMaskedId,
    string CustomerNameMasked,
    string CustomerPhoneMasked,
    string ZoneName,
    string Address,
    string DeliveryTimeWindow,
    int BoxCount,
    string Barcode,
    string? DeliveryNotes,
    IReadOnlyList<string> Allergies,
    IReadOnlyList<BoxMealSummaryItemDto> Meals);

public sealed record BoxMealSummaryItemDto(
    Guid MealId,
    string MealName,
    int Quantity,
    string Category,
    string? Notes);

public sealed record AssignBoxRequest(
    Guid DriverId,
    string? Notes = null);

public sealed record BulkAssignBoxesRequest(
    IReadOnlyList<Guid> BoxIds,
    Guid DriverId,
    string? Notes = null);

public sealed record AssignBoxResponse(
    Guid BoxId,
    string BoxCode,
    Guid DriverId,
    string DriverName,
    DateTime AssignedAtUtc,
    string Message);

