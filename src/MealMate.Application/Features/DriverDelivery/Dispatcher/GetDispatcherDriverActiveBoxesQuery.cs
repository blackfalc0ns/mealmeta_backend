using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetDispatcherDriverActiveBoxesQuery(Guid DriverId) : IQuery<DriverActiveBoxesResponseDto>;

public sealed class GetDispatcherDriverActiveBoxesQueryHandler : IQueryHandler<GetDispatcherDriverActiveBoxesQuery, DriverActiveBoxesResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;

    public GetDispatcherDriverActiveBoxesQueryHandler(
        IAppDbContext db,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _messages = messages;
    }

    public async Task<Result<DriverActiveBoxesResponseDto>> Handle(
        GetDispatcherDriverActiveBoxesQuery request,
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

        var isAr = _messages.IsArabic();

        var activeAssignments = await _db.DriverAssignments
            .AsNoTracking()
            .Include(da => da.Trip)
            .ThenInclude(t => t.Stops)
            .Where(da => da.DriverProfileId == driver.Id &&
                         (da.Trip.Status == TripStatus.InProgress || da.Trip.Status == TripStatus.Assigned))
            .ToListAsync(cancellationToken);

        var activeTrip = activeAssignments
            .OrderByDescending(da => da.AssignedAtUtc)
            .Select(da => da.Trip)
            .FirstOrDefault();

        var activeStops = activeTrip?.Stops
            .Where(s => s.Status == TripStopStatus.PickedUp || s.Status == TripStopStatus.Pending)
            .OrderBy(s => s.SequenceNumber)
            .ToList() ?? new();

        var outForDeliveryText = _messages.GetMessage(MessageCodes.DispatcherDriverDetails.StatusOutForDelivery, isAr ? "خارج للتوصيل" : "Out for Delivery");
        var receivedText = _messages.GetMessage(MessageCodes.DispatcherDriverDetails.StatusReceived, isAr ? "تم الاستلام" : "Picked Up");

        var boxes = new List<DriverActiveBoxDto>();

        for (int i = 0; i < activeStops.Count; i++)
        {
            var stop = activeStops[i];
            var isOutForDelivery = stop.Status == TripStopStatus.PickedUp || i == 0;

            var status = isOutForDelivery ? "InDelivery" : "PickedUp";
            var statusText = isOutForDelivery ? outForDeliveryText : receivedText;
            var statusColor = isOutForDelivery ? "Purple" : "Blue";

            var boxCode = stop.OrderCode.StartsWith("#") ? stop.OrderCode : $"#{stop.OrderCode}";
            var customerName = !string.IsNullOrWhiteSpace(stop.CustomerName) ? stop.CustomerName : stop.CustomerMaskedId;

            var scheduledTime = !string.IsNullOrWhiteSpace(stop.SlotLabel) ? stop.SlotLabel : "12:30 م";
            var zone = isAr ? stop.ZoneNameAr : (stop.ZoneNameEn ?? stop.ZoneNameAr);
            var address = $"{zone}، الكويت";

            boxes.Add(new DriverActiveBoxDto(
                BoxId: stop.Id,
                BoxCode: boxCode,
                Status: status,
                StatusText: statusText,
                StatusColor: statusColor,
                CustomerName: customerName,
                ScheduledTimeText: scheduledTime,
                DeliveryAddress: address));
        }

        return Result.Success(new DriverActiveBoxesResponseDto(driver.Id, boxes.Count, boxes));
    }
}
