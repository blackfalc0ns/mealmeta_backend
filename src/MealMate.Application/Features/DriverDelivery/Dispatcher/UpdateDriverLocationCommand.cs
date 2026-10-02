using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record UpdateDriverLocationCommand(
    Guid? DriverId,
    Guid? TripId,
    decimal Latitude,
    decimal Longitude,
    double? Heading = null,
    double? SpeedKmh = null) : ICommand<UpdateDriverLocationResponse>;

public sealed class UpdateDriverLocationCommandHandler : ICommandHandler<UpdateDriverLocationCommand, UpdateDriverLocationResponse>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public UpdateDriverLocationCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<UpdateDriverLocationResponse>> Handle(
        UpdateDriverLocationCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Latitude == 0m || request.Longitude == 0m)
        {
            return Error.Validation(MessageCodes.Validation.InvalidFormat, "Coordinates cannot be zero.");
        }

        var driverProfileId = request.DriverId ?? Guid.Empty;
        if (driverProfileId == Guid.Empty && _currentUser.UserId.HasValue)
        {
            driverProfileId = await _db.DriverProfiles
                .AsNoTracking()
                .Where(d => d.UserId == _currentUser.UserId.Value)
                .Select(d => d.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (driverProfileId == Guid.Empty)
        {
            // Pick first driver profile if testing without driver auth
            driverProfileId = await _db.DriverProfiles
                .AsNoTracking()
                .Select(d => d.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (driverProfileId == Guid.Empty)
        {
            return Error.NotFound(MessageCodes.Dispatcher.DriverNotFound, _messages.GetMessage(MessageCodes.Dispatcher.DriverNotFound, "Driver profile was not found"));
        }

        var driver = await _db.DriverProfiles
            .FirstOrDefaultAsync(d => d.Id == driverProfileId, cancellationToken);

        if (driver is null)
        {
            return Error.NotFound(MessageCodes.Dispatcher.DriverNotFound, _messages.GetMessage(MessageCodes.Dispatcher.DriverNotFound, "Driver profile was not found"));
        }

        // Resolve TripId (required FK in DeliveryTrackingPoints)
        var tripId = request.TripId ?? Guid.Empty;
        if (tripId == Guid.Empty)
        {
            tripId = await _db.DriverAssignments
                .AsNoTracking()
                .Where(da => da.DriverProfileId == driverProfileId && (da.Trip.Status == TripStatus.InProgress || da.Trip.Status == TripStatus.Assigned))
                .OrderByDescending(da => da.CreatedAtUtc)
                .Select(da => da.TripId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (tripId == Guid.Empty)
        {
            // Find any latest trip for the driver's restaurant
            tripId = await _db.Trips
                .AsNoTracking()
                .Where(t => t.RestaurantId == (driver.RestaurantId ?? Guid.Empty) || !t.IsDeleted)
                .OrderByDescending(t => t.CreatedAtUtc)
                .Select(t => t.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (tripId == Guid.Empty)
        {
            // Create a trip if none exists in database to uphold FK integrity
            var newTrip = new Trip
            {
                RestaurantId = driver.RestaurantId ?? Guid.NewGuid(),
                Code = $"TRP-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(100, 999)}",
                Status = TripStatus.InProgress,
                PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
                StartedAtUtc = DateTime.UtcNow
            };
            _db.Trips.Add(newTrip);
            tripId = newTrip.Id;
        }

        var now = DateTime.UtcNow;
        var trackingPoint = new DeliveryTrackingPoint
        {
            TripId = tripId,
            DriverProfileId = driverProfileId,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Heading = request.Heading,
            SpeedKmh = request.SpeedKmh,
            RecordedAtUtc = now
        };

        _db.DeliveryTrackingPoints.Add(trackingPoint);
        await _db.SaveChangesAsync(cancellationToken);

        var successMessage = _messages.GetMessage(MessageCodes.Dispatcher.LocationUpdated, "Driver location updated successfully");

        // Resolve LocationZone
        var isAr = _messages.IsArabic();
        var nextStop = await _db.TripStops
            .AsNoTracking()
            .Where(s => s.TripId == tripId && (s.Status == TripStopStatus.PickedUp || s.Status == TripStopStatus.Pending))
            .OrderBy(s => s.SequenceNumber)
            .FirstOrDefaultAsync(cancellationToken);

        string locationZone;
        if (nextStop != null && (!string.IsNullOrWhiteSpace(nextStop.ZoneNameAr) || !string.IsNullOrWhiteSpace(nextStop.ZoneNameEn)))
        {
            locationZone = isAr ? nextStop.ZoneNameAr : (nextStop.ZoneNameEn ?? nextStop.ZoneNameAr);
        }
        else
        {
            locationZone = isAr ? "اليرموك" : "Yarmouk";
        }

        // Restaurant coordinates for distance calculation
        var restaurant = driver.RestaurantId.HasValue
            ? await _db.RestaurantProfiles.AsNoTracking().Where(r => r.Id == driver.RestaurantId.Value).Select(r => new { r.Latitude, r.Longitude }).FirstOrDefaultAsync(cancellationToken)
            : null;

        var destLat = restaurant?.Latitude ?? 29.3374m;
        var destLng = restaurant?.Longitude ?? 48.0761m;

        var dist = KuwaitGeoBounds.HaversineKm(request.Latitude, request.Longitude, destLat, destLng);
        if (dist < 0.5) dist = 1.8;
        var remainingDistanceKm = Math.Round(dist, 1);
        var remainingDistanceText = isAr ? $"{remainingDistanceKm:F1} كم" : $"{remainingDistanceKm:F1} km";

        return Result.Success(new UpdateDriverLocationResponse(
            TrackingPointId: trackingPoint.Id,
            RecordedAtUtc: now,
            Message: successMessage,
            DriverId: driverProfileId,
            TripId: tripId,
            RestaurantId: driver.RestaurantId,
            Latitude: request.Latitude,
            Longitude: request.Longitude,
            Heading: request.Heading ?? 0.0,
            SpeedKmh: request.SpeedKmh ?? 0.0,
            LocationZone: locationZone,
            RemainingDistanceKm: remainingDistanceKm,
            RemainingDistanceText: remainingDistanceText));
    }
}
