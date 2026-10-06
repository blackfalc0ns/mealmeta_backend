namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record DispatcherDriversResponseDto(
    DriverCountsDto Counts,
    IReadOnlyList<DriverListItemDto> Items,
    DriverPaginationDto Pagination);

public sealed record DriverCountsDto(
    int Total,
    int Available,
    int InDelivery,
    int Unavailable);

public sealed record DriverListItemDto(
    Guid DriverId,
    string? DriverCode,
    string FullName,
    string? PhoneNumber,
    string? AvatarStorageKey,
    bool IsAvailable,
    string OperationalStatus,
    decimal? Rating,
    int RatingsCount,
    string? VehicleType,
    string? VehicleModel,
    string? VehiclePlate,
    string ShiftStatus,
    string CurrentStatusText);

public sealed record DriverPaginationDto(
    int PageNumber,
    int PageSize,
    int TotalItems,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);

public sealed record DispatcherDriverRosterDto(
    Guid? BoxId,
    DriverRosterCountsDto Counts,
    string SelectedView,
    string SelectedArea,
    string SectionTitle,
    IReadOnlyList<DriverRosterAreaChipDto> Areas,
    IReadOnlyList<DriverRosterItemDto> Drivers);

public sealed record DriverRosterCountsDto(
    int TotalCount,
    int AvailableCount,
    int BusyCount);

public sealed record DriverRosterAreaChipDto(
    string AreaKey,
    string Name,
    int DriverCount,
    bool IsSelected);

public sealed record DriverRosterItemDto(
    Guid DriverId,
    string DriverCode,
    string FullName,
    string? AvatarUrl,
    decimal Rating,
    string Status,
    string StatusText,
    string StatusDotColor,
    bool IsAvailableForSelection,
    string ActionText,
    bool ActionEnabled,
    int ActiveOrdersCount,
    string ActiveOrdersText,
    int CompletedOrdersTodayCount,
    string CompletedOrdersTodayText,
    double DistanceKm,
    string DistanceText,
    string CurrentZoneName);


