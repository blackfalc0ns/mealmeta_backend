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

        if (latestPoint is null)
        {
            var offlineStatus = !driver.IsAvailable
                ? _messages.GetMessage(MessageCodes.DispatcherRoster.StatusOnBreak, isAr ? "غير متصل / استراحة" : "Offline / On Break")
                : _messages.GetMessage(MessageCodes.DispatcherRoster.StatusAvailable, isAr ? "متاح (لا توجد إشارة)" : "Available (No Signal)");

            return Result.Success(new DriverCurrentLocationDto(
                DriverId: driver.Id,
                Latitude: null,
                Longitude: null,
                Heading: null,
                SpeedKmh: null,
                StatusBadge: offlineStatus,
                TimeAgoText: isAr ? "غير متوفر" : "Not available",
                StreetAddress: null,
                AreaCity: null,
                DestinationLatitude: null,
                DestinationLongitude: null,
                RoutePolyline: null));
        }

        decimal lat = latestPoint.Latitude;
        decimal lng = latestPoint.Longitude;
        DateTime recordedAt = latestPoint.RecordedAtUtc;

        var diffMinutes = (int)Math.Max(0, (DateTime.UtcNow - recordedAt).TotalMinutes);
        var timeAgoText = diffMinutes <= 1
            ? (isAr ? "آخر تحديث الآن" : "Last updated just now")
            : (isAr ? $"منذ {diffMinutes} دقيقة" : $"{diffMinutes} mins ago");

        string statusBadge;
        if (!driver.IsAvailable)
        {
            statusBadge = _messages.GetMessage(MessageCodes.DispatcherRoster.StatusOnBreak, isAr ? "في استراحة" : "On Break");
        }
        else if (currentStop != null)
        {
            statusBadge = _messages.GetMessage(MessageCodes.DispatcherDriverDetails.StatusOutForDelivery, isAr ? "خارج للتوصيل" : "Out for Delivery");
        }
        else
        {
            statusBadge = _messages.GetMessage(MessageCodes.DispatcherRoster.StatusAvailable, isAr ? "متاح" : "Available");
        }

        string? streetAddress = null;
        string? areaCity = null;
        decimal? destLat = null;
        decimal? destLng = null;
        string? polyline = null;

        if (currentStop != null)
        {
            string zone = isAr ? currentStop.ZoneNameAr : (currentStop.ZoneNameEn ?? currentStop.ZoneNameAr);
            streetAddress = zone;
            areaCity = zone;
            destLat = currentStop.Latitude;
            destLng = currentStop.Longitude;
            polyline = null;
        }

        return Result.Success(new DriverCurrentLocationDto(
            DriverId: driver.Id,
            Latitude: lat,
            Longitude: lng,
            Heading: null,
            SpeedKmh: null,
            StatusBadge: statusBadge,
            TimeAgoText: timeAgoText,
            StreetAddress: streetAddress,
            AreaCity: areaCity,
            DestinationLatitude: destLat,
            DestinationLongitude: destLng,
            RoutePolyline: polyline));
    }
}
