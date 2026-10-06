using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record DriverDeliveryHistoryStopDto(
    Guid StopId,
    Guid TripId,
    string OrderCode,
    string? BoxCode,
    string? CustomerName,
    string Zone,
    string? FullAddress,
    string SlotLabel,
    int SequenceNumber,
    int BoxCount,
    string StopStatus,
    DateTime? DeliveredAtUtc,
    string? FailureReason);

public sealed record DriverDeliveryHistoryTripDto(
    Guid TripId,
    string Code,
    DateOnly PlannedDate,
    string? ShiftName,
    string TripStatus,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<DriverDeliveryHistoryStopDto> Stops);

public sealed record DriverDeliveryHistoryResponseDto(
    Guid DriverId,
    int TripCount,
    IReadOnlyList<DriverDeliveryHistoryTripDto> Trips);

public sealed record GetDispatcherDriverDeliveryHistoryQuery(Guid DriverId, int Limit = 10)
    : IQuery<DriverDeliveryHistoryResponseDto>;

/// <summary>
/// Real past trips (with their stops) that the driver was assigned to, newest
/// first. Read-only history for admin driver profiles; no synthetic entries.
/// </summary>
public sealed class GetDispatcherDriverDeliveryHistoryQueryHandler
    : IQueryHandler<GetDispatcherDriverDeliveryHistoryQuery, DriverDeliveryHistoryResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser? _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public GetDispatcherDriverDeliveryHistoryQueryHandler(
        IAppDbContext db,
        ICurrentUser? currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public GetDispatcherDriverDeliveryHistoryQueryHandler(
        IAppDbContext db,
        ILocalizedMessageProvider messages)
        : this(db, null, messages)
    {
    }

    public async Task<Result<DriverDeliveryHistoryResponseDto>> Handle(
        GetDispatcherDriverDeliveryHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var driver = await _db.DriverProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DriverId, cancellationToken);

        if (driver is null)
        {
            return Error.NotFound(
                MessageCodes.Dispatcher.DriverNotFound,
                _messages.GetMessage(MessageCodes.Dispatcher.DriverNotFound, "Driver profile was not found."));
        }

        // Restaurant tenant isolation: cross-tenant driver queries return 404
        if (_currentUser != null && _currentUser.IsAuthenticated && _currentUser.RestaurantId.HasValue && _currentUser.RestaurantId.Value != Guid.Empty)
        {
            if (driver.RestaurantId != _currentUser.RestaurantId.Value)
            {
                return Error.NotFound(
                    MessageCodes.Dispatcher.DriverNotFound,
                    _messages.GetMessage(MessageCodes.Dispatcher.DriverNotFound, "Driver profile was not found."));
            }
        }

        var limit = Math.Clamp(request.Limit, 1, 50);
        var isAr = _messages.IsArabic();

        var assignments = await _db.DriverAssignments
            .AsNoTracking()
            .Include(da => da.Trip)
            .ThenInclude(t => t.Stops)
            .Where(da => da.DriverProfileId == driver.Id)
            .OrderByDescending(da => da.Trip.PlannedDate)
            .ThenByDescending(da => da.AssignedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var trips = assignments
            .Select(da => da.Trip)
            .Where(t => t is not null)
            .GroupBy(t => t.Id)
            .Select(g => g.First())
            .ToList();

        var stopIds = trips
            .SelectMany(t => t.Stops)
            .Select(s => s.Id)
            .ToList();

        var barcodeByStopId = await _db.MealBarcodes
            .AsNoTracking()
            .Where(b => b.TripStopId != null && stopIds.Contains(b.TripStopId.Value))
            .Select(b => new { b.TripStopId, b.BarcodeValue })
            .ToDictionaryAsync(x => x.TripStopId!.Value, x => x.BarcodeValue, cancellationToken);

        var tripDtos = trips
            .Select(trip =>
            {
                var stops = trip.Stops
                    .OrderBy(s => s.SequenceNumber)
                    .Select(s => new DriverDeliveryHistoryStopDto(
                        StopId: s.Id,
                        TripId: trip.Id,
                        OrderCode: s.OrderCode,
                        BoxCode: s.MealBarcodeId != null && barcodeByStopId.TryGetValue(s.MealBarcodeId.Value, out var byId)
                            ? byId
                            : (barcodeByStopId.TryGetValue(s.Id, out var byStop) ? byStop : null),
                        CustomerName: !string.IsNullOrWhiteSpace(s.CustomerName) ? s.CustomerName : s.CustomerMaskedId,
                        Zone: isAr ? s.ZoneNameAr : (s.ZoneNameEn ?? s.ZoneNameAr),
                        FullAddress: s.FullAddress,
                        SlotLabel: s.SlotLabel,
                        SequenceNumber: s.SequenceNumber,
                        BoxCount: s.BoxCount,
                        StopStatus: s.Status.ToString(),
                        DeliveredAtUtc: s.DeliveredAtUtc,
                        FailureReason: s.Status == TripStopStatus.Failed ? s.FailureReason : null))
                    .ToList();

                return new DriverDeliveryHistoryTripDto(
                    TripId: trip.Id,
                    Code: trip.Code,
                    PlannedDate: trip.PlannedDate,
                    ShiftName: trip.ShiftName,
                    TripStatus: trip.Status.ToString(),
                    StartedAtUtc: trip.StartedAtUtc,
                    CompletedAtUtc: trip.CompletedAtUtc,
                    Stops: stops);
            })
            .ToList();

        return Result.Success(new DriverDeliveryHistoryResponseDto(driver.Id, tripDtos.Count, tripDtos));
    }
}
