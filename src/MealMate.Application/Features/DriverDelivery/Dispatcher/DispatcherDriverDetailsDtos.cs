namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record DispatcherDriverDetailsResponseDto(
    DriverDetailsHeaderDto Driver,
    DriverTodayStatsDto Today,
    DriverVehicleDetailsDto Vehicle,
    DriverLocationDto CurrentLocation,
    DriverPerformanceDto Performance,
    IReadOnlyList<DriverDocumentDto> Documents);

public sealed record DriverDetailsHeaderDto(
    Guid DriverId,
    string? DriverCode,
    string FullName,
    string? PhoneNumber,
    string? AvatarStorageKey,
    bool IsAvailable,
    string OperationalStatus,
    DateTime? LastLocationAtUtc,
    string ShiftStatus,
    string CurrentStatusText);

public sealed record DriverTodayStatsDto(
    int CompletedDeliveries,
    int ActiveDeliveries,
    int FailedDeliveries,
    double DistanceKm);

public sealed record DriverVehicleDetailsDto(
    string? Type,
    string? Model,
    int? Year,
    string? Color,
    string? PlateNumber);

public sealed record DriverLocationDto(
    decimal? Latitude,
    decimal? Longitude,
    DateTime? RecordedAtUtc,
    double? Heading,
    double? SpeedKmh);

public sealed record DriverPerformanceDto(
    int TotalDelivered,
    decimal? AverageRating,
    int RatingsCount,
    double? OnTimeRatePercent,
    int ViolationsCount);

public sealed record DriverDocumentDto(
    string Type,
    string Status,
    string VerificationStatus,
    DateTime? ExpiresAtUtc);

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
