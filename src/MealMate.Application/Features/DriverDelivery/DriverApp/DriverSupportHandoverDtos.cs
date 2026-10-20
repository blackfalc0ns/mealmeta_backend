using MealMate.Application.Common.Messaging;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record CreateDriverSupportHandoverCommand(Guid IssueId, Guid ReplacementDriverId, string IdempotencyKey, string? Notes = null) : ICommand<DriverSupportHandoverDto>;
public sealed record ArriveDriverSupportHandoverCommand(Guid HandoverId, decimal Latitude, decimal Longitude) : ICommand<DriverSupportHandoverDto>;
public sealed record ScanDriverSupportHandoverBoxCommand(Guid HandoverId, string ManifestCode, string ReceiptStatus = "Received", string? FollowUpNote = null, string ScanMethod = "Barcode") : ICommand<DriverSupportHandoverDto>;
public sealed record CompleteDriverSupportHandoverCommand(Guid HandoverId) : ICommand<DriverSupportHandoverDto>;
public sealed record CancelDriverSupportHandoverCommand(Guid HandoverId, string? Reason = null) : ICommand<DriverSupportHandoverDto>;
public sealed record ResolveDriverSupportHandoverFollowUpCommand(Guid HandoverId, Guid TripStopId, string ManifestCode, string ResolutionNote) : ICommand<DriverSupportHandoverDto>;
public sealed record GetDriverSupportHandoverQuery(Guid HandoverId) : IQuery<DriverSupportHandoverDto>;
public sealed record GetMyDriverSupportHandoversQuery(string? Status = "active") : IQuery<IReadOnlyList<DriverSupportHandoverDto>>;
public sealed record GetDispatcherSupportHandoversQuery(string? Status = null, Guid? RestaurantId = null, int PageNumber = 1, int PageSize = 20) : IQuery<DispatcherSupportHandoversDto>;

public sealed record DriverSupportHandoverBoxDto(Guid TripStopId, string ManifestCode, string Status,
    DateTime? ReceivedAtUtc, string? FollowUpNote, Guid? FollowUpId = null, string? FollowUpStatus = null,
    DateTime? RecordedAtUtc = null);
public sealed record DriverSupportHandoverDto(
    Guid HandoverId, Guid IssueId, Guid TripId, Guid OriginalDriverId, Guid ReplacementDriverId,
    Guid RestaurantId, string Status, decimal PickupLatitude, decimal PickupLongitude,
    DateTime AssignedAtUtc, DateTime? ArrivedAtUtc, DateTime? CompletedAtUtc,
    IReadOnlyList<DriverSupportHandoverBoxDto> Boxes, Guid? ReplacementTripId = null,
    int ReceivedCount = 0, int MissingCount = 0, int DamagedCount = 0, int PendingCount = 0,
    string? PickupLocationSource = null, DateTime? PickupLocationRecordedAtUtc = null,
    string? IssueTitle = null, string? IssueCategory = null, string? IssueDescription = null, string? TripCode = null,
    string? OriginalDriverName = null, string? OriginalDriverPhone = null,
    string? ReplacementDriverName = null, string? ReplacementDriverPhone = null);
public sealed record DispatcherSupportHandoversDto(IReadOnlyList<DriverSupportHandoverDto> Items, int TotalCount, int PageNumber, int PageSize);
