namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record BoxTrackingHeaderDto(
    Guid BoxId,
    string BoxCode,
    string Status,
    string StatusText,
    string StatusColor,
    string CustomerName,
    string ScheduledTimeText,
    string DeliveryAddress);

public sealed record BoxTrackingTimelineStepDto(
    int Step,
    string Title,
    string Description,
    string? TimestampText,
    string State,
    string IconKind);

public sealed record BoxTrackingDriverDto(
    Guid DriverId,
    string DriverCode,
    string FullName,
    string? PhoneNumber,
    string? AvatarUrl);

public sealed record BoxTrackingDetailsDto(
    string ProgramType,
    string OrderDateText,
    string CustomerNotes,
    string MealsSummary);

public sealed record DispatcherBoxTrackingResponseDto(
    Guid BoxId,
    BoxTrackingHeaderDto Box,
    IReadOnlyList<BoxTrackingTimelineStepDto> Timeline,
    BoxTrackingDriverDto? Driver,
    BoxTrackingDetailsDto Details);

public sealed record ReportBoxIssueRequest(
    string IssueType,
    string Description,
    string? Severity = "Medium");

public sealed record ReportBoxIssueResponseDto(
    Guid IssueId,
    Guid BoxId,
    DateTime ReportedAtUtc,
    string Message);
