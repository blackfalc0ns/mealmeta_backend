namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record DriverPickupPointResponse(
    Guid TripId,
    string TripCode,
    Guid KitchenId,
    string KitchenName,
    string KitchenAddress,
    double? KitchenLatitude,
    double? KitchenLongitude,
    int TotalBoxesCount,
    string RouteDistanceText,
    string EstimatedArrivalMinutesText,
    bool HasArrivedAtKitchen,
    DateTime? ArrivedAtKitchenAtUtc);

public sealed record ArriveAtKitchenRequest(
    double? Latitude = null,
    double? Longitude = null);

public sealed record ArriveAtKitchenResponse(
    Guid TripId,
    string Message,
    DateTime ArrivedAtUtc);

public sealed record DriverPickupManifestResponse(
    Guid TripId,
    string TripCode,
    string KitchenName,
    int TotalBoxesCount,
    int ScannedBoxesCount,
    int RemainingBoxesCount,
    bool AllBoxesScanned,
    IReadOnlyList<DriverPickupBoxItemDto> Boxes)
{
    public int PickedUpBoxesCount => ScannedBoxesCount;
    public bool AllBoxesPickedUp => AllBoxesScanned;
}

public sealed record DriverPickupBoxItemDto(
    Guid BoxId,
    string BoxCode,
    string CustomerName,
    string DeliveryZone,
    int MealsCount,
    string MealsSummary,
    string DeliveryTimeSlot,
    string ScanStatus, // "PendingScan", "Scanned", "Damaged", "Missing"
    string ScanStatusText, // "لم يتم المسح", "تم المسح", "تالف", "مفقود"
    bool IsScanned,
    DateTime? ScannedAtUtc,
    string? ConditionPhotoStorageKey);

public sealed record ValidatePickupBarcodeResponse(
    bool IsValid,
    Guid? BoxId,
    string? BoxCode,
    string? CustomerName,
    string? DeliveryZone,
    int MealsCount,
    string? ScanStatus,
    string Message,
    bool BelongsToThisDriver,
    Guid? AssignedDriverId,
    string? AssignedDriverName,
    bool IsManualEntry = false,
    bool ConditionPhotoRequired = true);

public sealed record StartTripDeliveryRequest(
    double? Latitude = null,
    double? Longitude = null,
    bool ForceStartWithUnscannedBoxes = false,
    string? UnscannedReason = null);

public sealed record StartTripDeliveryResponse(
    Guid TripId,
    string TripCode,
    string Status,
    int DispatchedBoxesCount,
    string Message,
    DateTime StartedAtUtc);
