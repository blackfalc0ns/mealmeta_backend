namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record DispatcherDashboardOverviewDto(
    RestaurantContextDto Restaurant,
    DispatcherGreetingDto Greeting,
    DispatcherKpisDto Kpis,
    DispatcherOperationsStatusDto OperationsStatus,
    IReadOnlyList<TopDriverDto> TopDrivers,
    IReadOnlyList<RegionSummaryDto> RegionsSummary,
    ActiveIssuesContainerDto ActiveIssues);

public sealed record RestaurantContextDto(
    Guid Id,
    string Name,
    string NameAr,
    string NameEn,
    string Role);

public sealed record DispatcherGreetingDto(
    string Title,
    string Subtitle);

public sealed record DispatcherKpisDto(
    int TotalOrdersToday,
    int InDeliveryCount,
    int PendingAssignmentCount,
    int ActiveIssuesCount);

public sealed record DispatcherOperationsStatusDto(
    double CompletionRatePercentage,
    int DeliveredCount,
    int InDeliveryCount,
    int PendingAssignmentCount,
    int CancelledCount);

public sealed record TopDriverDto(
    Guid DriverId,
    string FullName,
    string? AvatarUrl,
    string Badge,
    decimal Rating,
    int CompletedDeliveriesToday);

public sealed record RegionSummaryDto(
    string RegionId,
    string RegionName,
    int OrdersCount,
    string Trend,
    double PercentageChange);

public sealed record ActiveIssuesContainerDto(
    int Count,
    string Summary,
    string SummaryAr,
    string SummaryEn,
    IReadOnlyList<ActiveIssueItemDto> Issues);

public sealed record ActiveIssueItemDto(
    string Id,
    string Type,
    string Severity,
    string? OrderId,
    Guid? DriverId,
    string? DriverName,
    string Message);

public sealed record LiveDriverLocationDto(
    Guid DriverId,
    string FullName,
    string? Phone,
    string? PlateNumber,
    string? AvatarUrl,
    string Status,
    string StatusLabel,
    string StatusLabelAr,
    string StatusLabelEn,
    decimal Latitude,
    decimal Longitude,
    double Heading,
    double SpeedKmh,
    string? ActiveOrderId,
    string? CustomerAddress,
    DateTime UpdatedAtUtc);

public sealed record UpdateDriverLocationRequest(
    Guid? DriverId,
    Guid? TripId,
    decimal Latitude,
    decimal Longitude,
    double? Heading,
    double? SpeedKmh);

public sealed record UpdateDriverLocationResponse(
    Guid TrackingPointId,
    DateTime RecordedAtUtc,
    string Message);

public sealed record LiveMapKpisDto(
    int ActiveDriversCount,
    int InDeliveryCount,
    int PausedCount,
    int AttentionRequiredCount);

public sealed record LiveMapDriverDto(
    Guid DriverId,
    string DriverCode,
    string DriverName,
    string? Phone,
    string? PlateNumber,
    string? AvatarUrl,
    string? BoxCode,
    decimal Latitude,
    decimal Longitude,
    double Heading,
    double SpeedKmh,
    string StatusCategory,
    string StatusText,
    string StatusColor,
    string TopIndicatorColor,
    string LocationZone,
    double? RemainingDistanceKm,
    string RemainingDistanceText,
    bool HasIssue,
    string? IssueDescription,
    DateTime UpdatedAtUtc);

public sealed record DispatcherLiveMapMonitoringDto(
    LiveMapKpisDto Kpis,
    IReadOnlyList<LiveMapDriverDto> Drivers);
