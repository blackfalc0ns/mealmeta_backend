namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record PerformanceKpisDto(
    int TotalBoxes,
    int DeliveredCount,
    decimal DeliveredPercentage,
    int AvgDelayMinutes,
    decimal OverallRating,
    int FailedCount,
    decimal FailedPercentage);

public sealed record PerformanceDistributionSegmentDto(
    string Key,
    string Title,
    int Count,
    decimal Percentage,
    string ColorHex);

public sealed record PerformanceDistributionDto(
    int TotalCount,
    IReadOnlyList<PerformanceDistributionSegmentDto> Segments);

public sealed record TopRatedDriverDto(
    int Rank,
    Guid DriverId,
    string DriverCode,
    string FullName,
    decimal Rating,
    string? AvatarUrl,
    bool IsHighlighted);

public sealed record DriverPerformanceRowDto(
    Guid DriverId,
    string DriverCode,
    string FullName,
    string? AvatarUrl,
    string Status,
    string StatusDotColor,
    int DeliveredCount,
    decimal DeliveredRate,
    string DeliveredRateText,
    int AvgDelayMinutes,
    string AvgDelayText,
    string AvgDelayColor,
    int FailedCount,
    decimal FailedRate,
    string FailedRateText,
    decimal Rating);

public sealed record DispatcherPerformanceOverviewResponseDto(
    string Period,
    string PeriodText,
    string DateRangeText,
    PerformanceKpisDto Kpis,
    PerformanceDistributionDto Distribution,
    IReadOnlyList<TopRatedDriverDto> TopDrivers,
    IReadOnlyList<DriverPerformanceRowDto> DriversTable);

public sealed record DriverComparisonMetricDto(
    Guid DriverId,
    string DriverCode,
    string FullName,
    string? AvatarUrl,
    int TotalAssigned,
    int DeliveredCount,
    decimal OnTimePercentage,
    int AvgDelayMinutes,
    decimal Rating,
    int FailedCount,
    decimal TotalDistanceKm);

public sealed record DispatcherDriverComparisonResponseDto(
    string Period,
    string PeriodText,
    IReadOnlyList<DriverComparisonMetricDto> Drivers);
