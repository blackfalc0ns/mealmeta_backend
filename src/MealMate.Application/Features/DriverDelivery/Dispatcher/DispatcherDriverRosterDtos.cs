namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record DispatcherDriverRosterDto(
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
    int ActiveOrdersCount,
    string ActiveOrdersText,
    int CompletedOrdersTodayCount,
    string CompletedOrdersTodayText,
    double DistanceKm,
    string DistanceText,
    string CurrentZoneName);
