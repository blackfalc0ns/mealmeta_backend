namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record DriverHomeScreenResponse(
    Guid DriverId,
    string DriverName,
    string DriverCode,
    string? ProfileImageUrl,
    string ShiftStatus, // Active, Inactive
    bool IsAvailable,
    string CurrentStatusText,
    string? NextLocationText,
    DriverTargetProgressDto? TargetProgress,
    DriverCurrentDeliveryTaskDto? CurrentDeliveryTask,
    DriverTodaySummaryDto? TodaySummary,
    DriverDailyPerformanceDto? DailyPerformance,
    DriverActiveSessionDto? ActiveSession,
    int UnreadNotificationsCount);

public sealed record DriverTargetProgressDto(
    int CompletedCount,
    int TargetCount,
    int RemainingCount,
    double ProgressPercentage,
    string PerformanceLevel,
    string SummaryText,
    string RemainingText);

public sealed record DriverCurrentDeliveryTaskDto(
    Guid BoxId,
    string BoxCode,
    string CustomerName,
    string? CustomerPhone,
    string DeliveryAddress,
    double? DestinationLatitude,
    double? DestinationLongitude,
    int MealsCount,
    string MealsSummary,
    string DeliveryTimeSlot,
    string Status, // Assigned, InTransit, Delivered
    string? DeliveryNotes);

public sealed record DriverTodaySummaryDto(
    int TotalOrders,
    int DeliveredOrders,
    int InDeliveryOrders,
    int IncompleteOrders);

public sealed record DriverDailyPerformanceDto(
    double OnTimeRatePercentage,
    double DistanceCoveredKm,
    double AverageDeliveryMinutes);

public sealed record DriverActiveSessionDto(
    Guid SessionId,
    DateTime StartedAtUtc,
    string WorkDurationText);

public sealed record DriverShiftSummaryResponse(
    Guid SessionId,
    string DriverName,
    string DriverCode,
    string ZoneName,
    string DateText,
    string WorkPeriodText,
    string WorkingHoursText,
    int TotalOrdersDelivered,
    double TotalDistanceKm,
    string CelebrationTitle,
    string CelebrationMessage,
    string MotivationalBadge);

public sealed record StartShiftRequest(
    double? Latitude = null,
    double? Longitude = null,
    string? ShiftLabel = null,
    string? Notes = null);

public sealed record EndShiftRequest(
    double? Latitude = null,
    double? Longitude = null,
    string? Notes = null,
    bool ForceEndWithPendingReturns = false);

public sealed record PickupBoxRequest(
    string? BarcodeValue = null,
    string? ConditionPhotoStorageKey = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    bool IsManualEntry = false,
    string? ManualBoxCode = null,
    string? ManualEntryReason = null);

public sealed record PickupBoxResponseDto(
    Guid BoxId,
    string BoxCode,
    string Status,
    string Message,
    DateTime PickedUpAtUtc,
    bool IsManualEntry = false);

public sealed record CompleteDeliveryRequest(
    string? BarcodeValue = null,
    string? ProofPhotoStorageKey = null,
    string? DeliveryOtp = null,
    string? HandoverNotes = null,
    decimal? Latitude = null,
    decimal? Longitude = null);

public sealed record CompleteDeliveryResponseDto(
    Guid BoxId,
    string BoxCode,
    string Status,
    string Message,
    DateTime DeliveredAtUtc);

public sealed record ReportIssueRequest(
    string IssueType,
    string Description,
    string? EvidencePhotoStorageKey = null,
    string Severity = "Medium");

public sealed record RegisterDeviceTokenRequest(
    string Token,
    string Platform = "Android",
    string? DeviceId = null);

public sealed record DriverNotificationItemDto(
    Guid NotificationId,
    string Type,
    string Title,
    string Body,
    string SentTimeAgo,
    bool IsRead,
    string? Section,
    Guid? SubjectId,
    string? DeepLink,
    DateTime SentAtUtc);

public sealed record DriverDeliveryMapResponse(
    Guid TripId,
    string TripCode,
    double? DriverLatitude,
    double? DriverLongitude,
    int TotalStopsCount,
    int CompletedStopsCount,
    DriverMapStopDto? FocusedStop,
    IReadOnlyList<DriverMapStopDto> Stops,
    IReadOnlyList<DriverMapWaypointDto> RoutePolylineWaypoints,
    DriverNavigationDto? Navigation = null);

public sealed record DriverMapStopDto(
    Guid StopId,
    string SequenceBadge,
    int SequenceNumber,
    string BoxCode,
    string CustomerName,
    string? CustomerPhone,
    string AddressShort,
    string FullAddress,
    int MealsCount,
    string MealsSummary,
    string DeliveryTimeSlot,
    string Status,
    string StatusText,
    string StatusColor,
    bool IsCurrent,
    double? Latitude,
    double? Longitude);

public sealed record DriverMapWaypointDto(
    double Latitude,
    double Longitude);

public sealed record CustomerArrivalResponseDto(
    Guid BoxId,
    string BoxCode,
    string CustomerName,
    string? CustomerPhone,
    string Status,
    string Message,
    DateTime ArrivedAtUtc);

public sealed record ReportDelayRequest(
    int DelayMinutes,
    string? Reason = null);

public sealed record FailDeliveryRequest(
    string ReasonCategory,
    string? Notes = null,
    string? ProofPhotoStorageKey = null,
    decimal? Latitude = null,
    decimal? Longitude = null);

public sealed record FailedDeliveryResponseDto(
    Guid BoxId,
    string BoxCode,
    string Status,
    string ReasonCategory,
    string Message,
    DateTime FailedAtUtc);

public sealed record ReturnBoxRequest(
    string? Reason = null,
    string? Notes = null,
    string? ReturnProofPhotoStorageKey = null,
    decimal? Latitude = null,
    decimal? Longitude = null);

public sealed record ReturnBoxResponseDto(
    Guid BoxId,
    string BoxCode,
    string Status,
    string Message,
    DateTime ReturnedAtUtc);

public sealed record RequestReassignmentRequest(
    string Reason,
    string? Notes = null,
    decimal? Latitude = null,
    decimal? Longitude = null);

public sealed record ReassignmentRequestResponseDto(
    Guid RequestId,
    Guid BoxId,
    string BoxCode,
    string Status,
    string Message,
    DateTime RequestedAtUtc);

public sealed record CustomerCallProxyResponseDto(
    Guid BoxId,
    string BoxCode,
    string CustomerName,
    string CustomerPhone,
    bool IsMasked,
    string? ProxyPhone,
    string? Extension,
    DateTime GeneratedAtUtc);

