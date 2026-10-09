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
    double? SpeedKmh = null,
    double? AccuracyMeters = null,
    DateTime? CapturedAtUtc = null) : ICommand<UpdateDriverLocationResponse>;

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
        // G03: measured accuracy drives contact eligibility — only fixes carrying a
        // plausible accuracy can qualify (DeliveryContactEligibilityService caps at 100m).
        // Clients that omit it are still tracked: the point is stored with a NULL accuracy
        // and the eligibility fix selection skips NULL-accuracy points, so a legacy ping
        // can never displace a quality fix. When provided, the value must be finite and
        // within (0, 10000].
        if (request.AccuracyMeters.HasValue
            && (!double.IsFinite(request.AccuracyMeters.Value)
                || request.AccuracyMeters.Value <= 0
                || request.AccuracyMeters.Value > 10_000))
        {
            return Error.Validation(
                MessageCodes.Validation.InvalidFormat,
                _messages.GetMessage(MessageCodes.Validation.InvalidFormat,
                    "accuracyMeters must be within (0, 10000] when provided."));
        }

        // Capture time is client-reported; anything in the future beyond a small skew
        // is rejected as a replay, and missing capture time falls back to ingest time.
        var capturedAt = request.CapturedAtUtc ?? DateTime.UtcNow;
        if (capturedAt > DateTime.UtcNow.AddMinutes(2))
        {
            return Error.Validation(
                MessageCodes.Validation.InvalidFormat,
                _messages.GetMessage(MessageCodes.Validation.InvalidFormat,
                    "capturedAtUtc cannot be in the future."));
        }

        // Coordinate validation is range-based: (0,0) is a geographically valid point
        // (AC17) and only out-of-range or non-finite values are rejected.
        if (!MealMate.Application.Features.VoiceCalls.Services.VoiceCallPolicyMath.IsValidLatitude((double)request.Latitude)
            || !MealMate.Application.Features.VoiceCalls.Services.VoiceCallPolicyMath.IsValidLongitude((double)request.Longitude))
        {
            return Error.Validation(MessageCodes.Validation.InvalidFormat, "Coordinates are out of range.");
        }

        // Driver identity: explicit DriverId (dispatcher route) or the JWT's own driver
        // profile (driver route / hub). Never fall back to an arbitrary profile — an
        // authenticated non-driver must not be able to move someone else's marker.
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
            return Error.NotFound(MessageCodes.Dispatcher.DriverNotFound, _messages.GetMessage(MessageCodes.Dispatcher.DriverNotFound, "Driver profile was not found"));
        }

        var driver = await _db.DriverProfiles
            .FirstOrDefaultAsync(d => d.Id == driverProfileId, cancellationToken);

        if (driver is null)
        {
            return Error.NotFound(MessageCodes.Dispatcher.DriverNotFound, _messages.GetMessage(MessageCodes.Dispatcher.DriverNotFound, "Driver profile was not found"));
        }

        // Location streaming gate: tracking is only accepted while the driver actually has
        // work in hand (pending/picked-up stops on an assigned or in-progress trip) — the
        // same definition the dispatcher active-boxes screen uses. A driver with no active
        // deliveries must not stream location.
        var hasActiveWork = await _db.TripStops
            .AsNoTracking()
            .AnyAsync(s => s.Trip.DriverAssignments.Any(da => da.DriverProfileId == driverProfileId &&
                                                              (da.Trip.Status == TripStatus.InProgress || da.Trip.Status == TripStatus.Assigned)) &&
                          (s.Status == TripStopStatus.PickedUp || s.Status == TripStopStatus.Pending),
                cancellationToken);

        if (!hasActiveWork)
        {
            return Error.Conflict(
                MessageCodes.Dispatcher.TrackingNotRequired,
                _messages.GetMessage(MessageCodes.Dispatcher.TrackingNotRequired, "Location tracking is not active because the driver has no assigned boxes or active deliveries"));
        }

        // Resolve TripId (required FK in DeliveryTrackingPoints): an explicitly supplied
        // trip must belong to one of THIS driver's active assignments, otherwise the
        // newest active-assignment trip is used. There is deliberately no unrelated-trip
        // fallback and no fabricated trip creation (AC16) - a driver with no active
        // work is already rejected above, so reaching this point without a real trip
        // is a state inconsistency reported as a conflict, never silent fake data.
        Guid? tripId = null;
        if (request.TripId.HasValue && request.TripId.Value != Guid.Empty)
        {
            var ownsRequestedTrip = await _db.DriverAssignments
                .AsNoTracking()
                .AnyAsync(da => da.DriverProfileId == driverProfileId
                                && da.Status == "Active"
                                && da.TripId == request.TripId.Value
                                && (da.Trip.Status == TripStatus.InProgress || da.Trip.Status == TripStatus.Assigned),
                    cancellationToken);

            if (!ownsRequestedTrip)
            {
                return Error.Conflict(
                    MessageCodes.Dispatcher.TrackingNotRequired,
                    _messages.GetMessage(MessageCodes.Dispatcher.TrackingNotRequired, "The requested trip is not an active assignment for this driver"));
            }

            tripId = request.TripId.Value;
        }
        else
        {
            tripId = await _db.DriverAssignments
                .AsNoTracking()
                .Where(da => da.DriverProfileId == driverProfileId
                             && da.Status == "Active"
                             && (da.Trip.Status == TripStatus.InProgress || da.Trip.Status == TripStatus.Assigned))
                .OrderByDescending(da => da.AssignedAtUtc)
                .Select(da => (Guid?)da.TripId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (tripId is null || tripId.Value == Guid.Empty)
        {
            return Error.Conflict(
                MessageCodes.Dispatcher.TrackingNotRequired,
                _messages.GetMessage(MessageCodes.Dispatcher.TrackingNotRequired, "No active trip is assigned to this driver"));
        }

        var now = DateTime.UtcNow;
        var trackingPoint = new DeliveryTrackingPoint
        {
            TripId = tripId.Value,
            DriverProfileId = driverProfileId,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Heading = request.Heading,
            SpeedKmh = request.SpeedKmh,
            AccuracyMeters = request.AccuracyMeters,
            // Real client capture time (validated against replay above); ReceivedAtUtc
            // stays server-authoritative ingest time for the captured+received age rule.
            CapturedAtUtc = capturedAt,
            ReceivedAtUtc = now,
            RecordedAtUtc = now
        };

        _db.DeliveryTrackingPoints.Add(trackingPoint);
        await _db.SaveChangesAsync(cancellationToken);

        var successMessage = _messages.GetMessage(MessageCodes.Dispatcher.LocationUpdated, "Driver location updated successfully");

        // Resolve the next real stop for zone and distance display; both are null when
        // no next stop exists - no fabricated zone name, no invented distance.
        var isAr = _messages.IsArabic();
        var nextStop = await _db.TripStops
            .AsNoTracking()
            .Where(s => s.TripId == tripId.Value && (s.Status == TripStopStatus.PickedUp || s.Status == TripStopStatus.Pending))
            .OrderBy(s => s.SequenceNumber)
            .FirstOrDefaultAsync(cancellationToken);

        string? locationZone = null;
        double? remainingDistanceKm = null;
        string? remainingDistanceText = null;

        if (nextStop != null && (!string.IsNullOrWhiteSpace(nextStop.ZoneNameAr) || !string.IsNullOrWhiteSpace(nextStop.ZoneNameEn)))
        {
            locationZone = isAr ? nextStop.ZoneNameAr : (nextStop.ZoneNameEn ?? nextStop.ZoneNameAr);
        }

        if (nextStop?.Latitude.HasValue == true && nextStop.Longitude.HasValue)
        {
            var meters = MealMate.Application.Features.VoiceCalls.Services.VoiceCallPolicyMath.HaversineMeters(
                (double)request.Latitude,
                (double)request.Longitude,
                (double)nextStop.Latitude.Value,
                (double)nextStop.Longitude.Value);

            if (meters.HasValue)
            {
                remainingDistanceKm = Math.Round(meters.Value / 1000d, 1);
                remainingDistanceText = isAr ? $"{remainingDistanceKm.Value:F1} كم" : $"{remainingDistanceKm.Value:F1} km";
            }
        }

        return Result.Success(new UpdateDriverLocationResponse(
            TrackingPointId: trackingPoint.Id,
            RecordedAtUtc: now,
            Message: successMessage,
            DriverId: driverProfileId,
            TripId: tripId.Value,
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
