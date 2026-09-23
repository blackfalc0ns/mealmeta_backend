namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record DriverHeaderDto(
    Guid DriverId,
    string DriverCode,
    string FullName,
    string? PhoneNumber,
    string? AvatarUrl,
    string Status,
    string StatusText,
    string StatusDotColor,
    string LastUpdatedText);

public sealed record DriverKpisDto(
    decimal Rating,
    int AvgDelayMinutes,
    int CompletedTodayCount,
    int ActiveBoxesCount);

public sealed record DriverDailySummaryDto(
    int ApproxDistanceKm,
    int AvgDelayMinutes,
    int FailedDeliveriesCount,
    int CompletedDeliveriesCount);

public sealed record DispatcherDriverDetailsDto(
    DriverHeaderDto Driver,
    DriverKpisDto Kpis,
    DriverDailySummaryDto DailySummary);

public sealed record DriverActiveBoxDto(
    Guid BoxId,
    string BoxCode,
    string Status,
    string StatusText,
    string StatusColor,
    string CustomerName,
    string ScheduledTimeText,
    string DeliveryAddress);

public sealed record DriverActiveBoxesResponseDto(
    Guid DriverId,
    int TotalCount,
    IReadOnlyList<DriverActiveBoxDto> Boxes);

public sealed record DriverCurrentLocationDto(
    Guid DriverId,
    decimal? Latitude,
    decimal? Longitude,
    double? Heading,
    double? SpeedKmh,
    string StatusBadge,
    string TimeAgoText,
    string? StreetAddress,
    string? AreaCity,
    decimal? DestinationLatitude,
    decimal? DestinationLongitude,
    string? RoutePolyline = null);
