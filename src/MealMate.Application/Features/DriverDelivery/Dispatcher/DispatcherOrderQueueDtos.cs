namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record DispatcherOrderQueueDto(
    RestaurantContextDto Restaurant,
    DispatcherQueueCountsDto Counts,
    IReadOnlyList<DispatcherQueueBoxItemDto> Boxes);

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
    decimal Rating,
    bool IsRecommended,
    string? RecommendationReason);

public sealed record AssignBoxRequest(
    Guid DriverId,
    string? Notes = null);

public sealed record AssignBoxResponse(
    Guid BoxId,
    string BoxCode,
    Guid DriverId,
    string DriverName,
    DateTime AssignedAtUtc,
    string Message);
