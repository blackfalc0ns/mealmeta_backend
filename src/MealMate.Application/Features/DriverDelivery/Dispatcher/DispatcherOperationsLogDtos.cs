namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record OperationsLogCountersDto(
    int AllCount,
    int CompletedCount,
    int CancelledCount,
    int FailedCount,
    int ReassignedCount);

public sealed record OperationsLogDriverDto(
    Guid Id,
    string Name,
    string? AvatarUrl,
    string IndicatorColor);

public sealed record OperationsLogCustomerDto(
    Guid? Id,
    string Name,
    string LabelText,
    string Area,
    string AddressText);

public sealed record OperationsLogEntryDto(
    Guid Id,
    Guid BoxId,
    string BoxCode,
    string Type, // Completed, Reassigned, Failed, CancelledByRestaurant
    string StatusText,
    string StatusColor,
    string StatusIcon,
    OperationsLogDriverDto? Driver,
    OperationsLogDriverDto? OriginalDriver,
    OperationsLogDriverDto? ReplacementDriver,
    string? CancelledBy,
    string? CancelledByText,
    OperationsLogCustomerDto Customer,
    string TimeText,
    DateTime OccurredAtUtc);

public sealed record OperationsLogPaginationDto(
    int PageNumber,
    int PageSize,
    int TotalItems,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);

public sealed record DispatcherOperationsLogResponseDto(
    OperationsLogCountersDto Counters,
    IReadOnlyList<OperationsLogEntryDto> Operations,
    OperationsLogPaginationDto Pagination);
