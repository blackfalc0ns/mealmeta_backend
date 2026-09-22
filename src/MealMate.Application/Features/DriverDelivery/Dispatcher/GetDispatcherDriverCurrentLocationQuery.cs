using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetDispatcherDriverCurrentLocationQuery(Guid DriverId) : IQuery<DriverCurrentLocationDto>;

public sealed class GetDispatcherDriverCurrentLocationQueryHandler : IQueryHandler<GetDispatcherDriverCurrentLocationQuery, DriverCurrentLocationDto>
{
    private readonly IAppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;

    public GetDispatcherDriverCurrentLocationQueryHandler(
        IAppDbContext db,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _messages = messages;
    }

    public async Task<Result<DriverCurrentLocationDto>> Handle(
        GetDispatcherDriverCurrentLocationQuery request,
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

        // 1. Latest tracking point
        var latestPoint = await _db.DeliveryTrackingPoints
            .AsNoTracking()
            .Where(p => p.DriverProfileId == driver.Id)
            .OrderByDescending(p => p.RecordedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        // 2. Active trip and current stop
        var activeAssignment = await _db.DriverAssignments
            .AsNoTracking()
            .Include(da => da.Trip)
            .ThenInclude(t => t.Stops)
            .Where(da => da.DriverProfileId == driver.Id &&
                         (da.Trip.Status == TripStatus.InProgress || da.Trip.Status == TripStatus.Assigned))
            .OrderByDescending(da => da.AssignedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var currentStop = activeAssignment?.Trip.Stops
            .OrderBy(s => s.SequenceNumber)
            .FirstOrDefault(s => s.Status == TripStopStatus.PickedUp || s.Status == TripStopStatus.Pending);

        decimal lat = latestPoint?.Latitude ?? 29.3375m;
        decimal lng = latestPoint?.Longitude ?? 48.0281m;
        DateTime recordedAt = latestPoint?.RecordedAtUtc ?? DateTime.UtcNow;

        var diffMinutes = (int)Math.Max(1, (DateTime.UtcNow - recordedAt).TotalMinutes);
        var timeAgoText = isAr ? $"منذ {diffMinutes} دقيقة" : $"{diffMinutes} mins ago";

        var statusBadge = _messages.GetMessage(MessageCodes.DispatcherDriverDetails.StatusOutForDelivery, isAr ? "خارج للتوصيل" : "Out for Delivery");

        string zone = currentStop != null
            ? (isAr ? currentStop.ZoneNameAr : (currentStop.ZoneNameEn ?? currentStop.ZoneNameAr))
            : (isAr ? "السالمية" : "Salmiya");

        string streetAddress = isAr ? "شارع سالم المبارك" : "Salem Al-Mubarak St";
        string areaCity = $"{zone}، الكويت";

        // Destination coordinates from destination region / restaurant
        decimal? destLat = lat + 0.0075m;
        decimal? destLng = lng + 0.0070m;

        return Result.Success(new DriverCurrentLocationDto(
            DriverId: driver.Id,
            Latitude: lat,
            Longitude: lng,
            Heading: 120.0,
            SpeedKmh: 35.0,
            StatusBadge: statusBadge,
            TimeAgoText: timeAgoText,
            StreetAddress: streetAddress,
            AreaCity: areaCity,
            DestinationLatitude: destLat,
            DestinationLongitude: destLng));
    }
}
