namespace MealMate.Application.Features.AdminCoverage;

public sealed record MapPointDto(decimal Latitude, decimal Longitude);

public sealed record CoverageFiltersDto(
    string CountryCode,
    Guid? GovernorateId,
    Guid? RestaurantId,
    DateOnly ServiceDate,
    string Search);

public sealed record CoverageRegionDto(
    Guid Id,
    Guid? ParentId,
    string Type,
    string NameAr,
    string NameEn,
    bool IsActive,
    MapPointDto? Point,
    bool IsScopeContextOnly);

public sealed record CoverageCapacityDto(int Limit, int Confirmed, bool Available);

public sealed record CoverageRestaurantDto(
    Guid Id,
    string NameAr,
    string NameEn,
    string? LogoUrl,
    bool OperationallyActive,
    bool AccountActive,
    MapPointDto? Point,
    CoverageCapacityDto? Capacity);

public sealed record CoverageLinkDto(
    Guid RestaurantId,
    Guid RegionId,
    bool IsActive,
    string DeliveryWindowStart,
    string DeliveryWindowEnd);

public sealed record CoverageMetricsDto(
    int ActiveAreaCount,
    int FullyCoveredAreaCount,
    int PartiallyCoveredAreaCount,
    int UncoveredAreaCount,
    int SingleRestaurantAreaCount,
    decimal? FullyCoveredAreaPercent);

public sealed record CoverageSnapshotDto(
    DateTime GeneratedAtUtc,
    CoverageFiltersDto Filters,
    IReadOnlyList<CoverageRegionDto> Regions,
    IReadOnlyList<CoverageRestaurantDto> Restaurants,
    IReadOnlyList<CoverageLinkDto> Links,
    CoverageMetricsDto Metrics);

public sealed record DriverMapLocationDto(
    Guid DriverId,
    Guid RestaurantId,
    Guid TripId,
    MapPointDto? Point,
    DateTime? RecordedAtUtc,
    double? Heading,
    double? SpeedKmh);

public sealed record OperationsTripDto(
    Guid Id,
    string Code,
    Guid RestaurantId,
    DateOnly PlannedDate,
    string Status,
    Guid? DriverId,
    int StopCount,
    int DeliveredStopCount,
    int FailedStopCount);

public sealed record OperationsTripStopDto(
    Guid Id,
    int SequenceNumber,
    string Status,
    MapPointDto? Point,
    string OrderCode,
    string CustomerMaskedId = "",
    int BoxCount = 1,
    string ZoneNameAr = "",
    string ZoneNameEn = "");

public sealed record OperationsTripDetailDto(
    Guid Id,
    string Code,
    Guid RestaurantId,
    DateOnly PlannedDate,
    string Status,
    Guid? DriverId,
    int StopCount,
    int DeliveredStopCount,
    int FailedStopCount,
    IReadOnlyList<OperationsTripStopDto> Stops,
    IReadOnlyList<RecordedMapPointDto> ActualTrack,
    bool ActualTrackWasTruncated,
    int ActualTrackSourceCount,
    string ActualTrackSamplingStrategy);

public sealed record RecordedMapPointDto(decimal Latitude, decimal Longitude, DateTime RecordedAtUtc, Guid DriverId, int SegmentIndex);

public sealed record PlannedTripRouteLegDto(
    int Sequence,
    Guid? FromStopId,
    Guid? ToStopId,
    MapPointDto? Origin,
    MapPointDto? Destination,
    string? EncodedPolyline,
    long? DistanceMeters,
    long? DurationSeconds,
    bool IsFallbackEstimate,
    string? ErrorCode);

public sealed record PlannedTripRouteDto(
    Guid TripId,
    DateTime GeneratedAtUtc,
    string Provider,
    IReadOnlyList<PlannedTripRouteLegDto> Legs,
    IReadOnlyList<Guid> MissingStopIds,
    long? TotalDistanceMeters,
    long? TotalDurationSeconds,
    bool HasFallbackEstimates,
    bool IsPartial,
    string? ErrorCode,
    string OriginMode = "dispatch",
    DateTime? SourceLocationRecordedAtUtc = null,
    IReadOnlyList<Guid>? RemainingStopIds = null);

public sealed record OperationsTripPageDto(
    DateTime GeneratedAtUtc,
    IReadOnlyList<OperationsTripDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int InProgressCount,
    int ScheduledCount);

public sealed record DriverBoxTotalsDto(
    int StopCount, int DeliveredStopCount, int RemainingStopCount, int FailedStopCount, int ReturnedStopCount,
    int DeliveredBoxCount, int RemainingBoxCount, int FailedBoxCount, int ReturnedBoxCount, int InvalidBoxCount);

public sealed record AdminCoverageDriverTripDto(
    Guid TripId, string Code, DateOnly PlannedDate, string Status, DriverBoxTotalsDto Totals);

public sealed record AdminCoverageDriverRosterDto(
    Guid DriverId, Guid? RestaurantId, string FullNameAr, string FullNameEn, string? ProfileImageUrl,
    DateTime ShiftStartedAtUtc, MapPointDto? CurrentPoint, DateTime? LocationRecordedAtUtc,
    IReadOnlyList<AdminCoverageDriverTripDto> Trips,
    bool IsAvailable = true,
    Guid? CurrentTripId = null,
    string? CurrentTripStatus = null,
    string? NextZoneNameAr = null,
    string? NextZoneNameEn = null);

public sealed record DriverMapLocationPageDto(
    DateTime GeneratedAtUtc,
    IReadOnlyList<DriverMapLocationDto> Items,
    IReadOnlyList<AdminCoverageDriverRosterDto> Drivers)
{
    public DriverMapLocationPageDto(DateTime generatedAtUtc, IReadOnlyList<DriverMapLocationDto> items)
        : this(generatedAtUtc, items, Array.Empty<AdminCoverageDriverRosterDto>())
    {
    }
}
