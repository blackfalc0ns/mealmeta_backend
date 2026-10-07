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
    double? SpeedKmh,
    double? AccuracyMeters = null,
    DateTime? CapturedAtUtc = null);

public sealed record UpdateDriverLocationResponse(
    Guid TrackingPointId,
    DateTime RecordedAtUtc,
    string Message,
    Guid? DriverId = null,
    Guid? TripId = null,
    Guid? RestaurantId = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    double? Heading = null,
    double? SpeedKmh = null,
    string? LocationZone = null,
    double? RemainingDistanceKm = null,
    string? RemainingDistanceText = null);

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
    decimal? Latitude,
    decimal? Longitude,
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

public sealed record SimulateLocationEventRequest(
    Guid? RestaurantId,
    Guid DriverId,
    Guid? TripId,
    decimal Latitude,
    decimal Longitude,
    double? Heading,
    double? SpeedKmh,
    string? LocationZone,
    double? RemainingDistanceKm,
    string? RemainingDistanceText,
    DateTime? TimestampUtc);

public sealed record SimulateStatusEventRequest(
    Guid? RestaurantId,
    Guid DriverId,
    string StatusCategory,
    string? StatusText,
    string? StatusColor,
    string? TopIndicatorColor,
    bool HasIssue,
    string? IssueDescription,
    DateTime? TimestampUtc);

public sealed record SimulateIssueEventRequest(
    Guid? RestaurantId,
    Guid? IssueId,
    Guid DriverId,
    bool Resolved,
    string? Status,
    string? IssueDescription,
    DateTime? TimestampUtc);
