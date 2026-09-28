namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record ValidatePickupBarcodeRequest(
    string BarcodeValue,
    bool IsManualEntry = false);

public sealed record ValidatePickupBoxDto(
    Guid BoxId,
    string BoxCode,
    string CustomerName,
    string DeliveryZone,
    int MealsCount);

public sealed record ValidatePickupBarcodeResponseDto(
    bool IsValid,
    string ValidationToken,
    DateTime ExpiresAtUtc,
    ValidatePickupBoxDto Box,
    string ScanStatus,
    string NextAction)
{
    public Guid BoxId => Box.BoxId;
}

public sealed record UploadPickupConditionPhotoResponseDto(
    Guid BoxId,
    string StorageKey,
    string ContentType,
    long SizeBytes,
    DateTime UploadedAtUtc,
    string ScanStatus,
    string NextAction)
{
    public string ConditionPhotoStorageKey => StorageKey;
}

public sealed record ConfirmPickupBoxRequest(
    string ValidationToken,
    string ConditionPhotoStorageKey,
    decimal Latitude,
    decimal Longitude);

public sealed record ConfirmPickupBoxResponseDto(
    Guid TripId,
    Guid BoxId,
    string BoxCode,
    string Status,
    DateTime PickedUpAtUtc,
    int AssignedBoxesCount,
    int ValidatedBoxesCount,
    int ReceivedBoxesCount,
    int RemainingBoxesCount,
    bool AllBoxesReceived,
    string NextAction)
{
    public int TotalBoxesCount => AssignedBoxesCount;
    public int PickedUpBoxesCount => ReceivedBoxesCount;
    public bool AllBoxesPickedUp => AllBoxesReceived;
}

public sealed record TripPickupSummaryBoxDto(
    Guid BoxId,
    string BoxCode,
    string CustomerName,
    string Status,
    DateTime? ValidatedAtUtc,
    DateTime? PickedUpAtUtc,
    string? ConditionPhotoUrl);

public sealed record TripPickupSummaryResponseDto(
    Guid TripId,
    string TripCode,
    string TripStatus,
    string PickupStatus,
    int AssignedBoxesCount,
    int ValidatedBoxesCount,
    int ReceivedBoxesCount,
    int RemainingBoxesCount,
    bool AllBoxesReceived,
    bool CanStartTrip,
    IReadOnlyList<TripPickupSummaryBoxDto> Boxes)
{
    public int TotalBoxesCount => AssignedBoxesCount;
    public int PickedUpBoxesCount => ReceivedBoxesCount;
    public bool AllBoxesPickedUp => AllBoxesReceived;
}

public sealed record StartTripRequest(
    decimal Latitude,
    decimal Longitude);

public sealed record StartTripResponseDto(
    Guid TripId,
    string TripCode,
    string Status,
    int DispatchedBoxesCount,
    string Message,
    DateTime StartedAtUtc);
