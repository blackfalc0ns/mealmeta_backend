namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record DriverHomeScreenResponse(
    Guid DriverId,
    string DriverName,
    string DriverCode,
    string? ProfileImageUrl,
    string ShiftStatus, // Active, Inactive (display values); session-internal Ended/Cancelled are never exposed
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

/// <summary>BarcodeValue must contain the QR scanned again at delivery, matching the persisted pickup scan.</summary>
public sealed record CompleteDeliveryRequest(
    string? BarcodeValue = null,
    string? ProofPhotoStorageKey = null,
    string? DeliveryOtp = null,
    string? HandoverNotes = null,
    decimal? Latitude = null,
    decimal? Longitude = null);

/// <summary>
/// Delivery confirmation response. <see cref="TripId"/> identifies the stop's own
/// trip (the map list can aggregate several active trips, so completion counts are
/// always scoped to this trip). <see cref="RemainingStopsCount"/> counts the stops
/// of the same trip that are still neither Delivered nor Failed after this call —
/// the same final-state rule that completes the trip. Repeated deliver calls on
/// an already-delivered stop succeed idempotently: <see cref="DeliveredAtUtc"/>
/// stays the original instant, <see cref="IsFirstDelivery"/> is false, and no
/// counter, scan record, or notification is repeated.
/// Note: the request's deliveryOtp field is currently NOT verified by the
/// backend; delivery requires the pickup QR again and a proof photo. See the screens-07 contract
/// documentation before advertising OTP support in any UI.
/// </summary>
public sealed record CompleteDeliveryResponseDto(
    Guid BoxId,
    string BoxCode,
    string Status,
    string Message,
    DateTime DeliveredAtUtc,
    Guid? TripId = null,
    bool IsTripCompleted = false,
    int RemainingStopsCount = 0,
    Guid? DriverId = null,
    bool IsFirstDelivery = false);

/// <summary>
/// Driver report-issue request (screen 08.01). <see cref="IssueType"/> is required
/// and must be one of <see cref="DriverIssueCatalog.IssueTypes"/> (case-insensitive;
/// persisted in canonical form). <see cref="Description"/> is optional and capped
/// at 250 characters — matching the design's counter. Severity is optional
/// (Low/Medium/High, default Medium). <see cref="EvidencePhotoStorageKeys"/> holds
/// up to 3 uploaded evidence photos; the legacy singular
/// <see cref="EvidencePhotoStorageKey"/> is still accepted and is merged first
/// (duplicates removed, empty strings ignored, more than 3 distinct keys rejected).
/// </summary>
public sealed record ReportIssueRequest(
    string IssueType,
    string? Description = null,
    string? EvidencePhotoStorageKey = null,
    string[]? EvidencePhotoStorageKeys = null,
    string Severity = "Medium",
    string? IdempotencyKey = null);

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

/// <summary>
/// Live route map for the driver delivery-map screen.
/// <see cref="RoutePolylineWaypoints"/> holds the customer stop pins only — it is
/// NOT a street route and must never be drawn as one; the real road geometry is
/// <see cref="DriverNavigationDto.EncodedPolyline"/> inside <see cref="Navigation"/>.
/// <see cref="CustomerPhone"/> on every stop is always empty for the driver: the
/// driver app builds contact actions from the call-proxy API, not stored phones.
/// </summary>
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

/// <summary>
/// One customer stop card on the driver delivery map. <see cref="BoxId"/> is the
/// same identifier as <see cref="StopId"/> (the driver order commands address the
/// stop by its id — the alias makes the relation explicit for the mobile team);
/// it is NOT a DailyOrderId. <see cref="TripId"/> identifies which trip the stop
/// belongs to when the carousel aggregates stops across several active trips.
/// <see cref="CustomerNote"/> comes from the delivery address chosen for the
/// subscription day; null when the address carries no note. <see cref="ArrivedAtUtc"/>
/// is the persisted first-arrival time (null until arrive-customer succeeds).
/// There is intentionally no numeric delivery window and no delay flag here: the
/// only slot source today is the display-only SlotLabel text, which must never be
/// parsed into a UTC commitment (documented in the screens-07 contract).
/// </summary>
public sealed record DriverMapStopDto(
    Guid StopId,
    Guid BoxId,
    Guid TripId,
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
    double? Longitude,
    string? CustomerNote = null,
    DateTime? ArrivedAtUtc = null);

public sealed record DriverMapWaypointDto(
    double Latitude,
    double Longitude);

/// <summary>
/// Arrival confirmation for the assigned driver. Deliberately carries no customer
/// phone: contact goes through the in-app voice call or the audited reveal flow.
/// <see cref="ArrivedAtUtc"/> is the persisted first-arrival time: a repeated
/// arrive call returns the same instant with <see cref="IsFirstArrival"/> = false,
/// so screen reloads never move the arrival time. Only the stop's assigned driver
/// (active trip assignment) can save or read this state.
/// </summary>
public sealed record CustomerArrivalResponseDto(
    Guid BoxId,
    string BoxCode,
    string CustomerName,
    string Status,
    string Message,
    DateTime ArrivedAtUtc,
    Guid? DriverId = null,
    bool IsFirstArrival = false);

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

/// <summary>
/// Driver reassignment request (screen 08.02). <see cref="Reason"/> is required
/// and must be one of <see cref="DriverIssueCatalog.ReassignmentReasons"/> (the
/// design's fixed dropdown; case-insensitive, persisted canonically). Notes are
/// optional, capped at 250 characters. The coordinate pair is optional: send both
/// or neither; when present they are range-checked and persisted with the request.
/// </summary>
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

