using System.Globalization;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;

namespace MealMate.Application.Features.DriverDelivery.Trips;

/// <summary>
/// Normalizes persisted trip/stop lifecycle evidence into ordered timeline
/// events. Only real timestamps become events — no event is inferred from the
/// current status and no missing time is invented.
/// </summary>
public interface ITripTimelineBuilder
{
    IReadOnlyList<TripTimelineEventDto> Build(TripTimelineRequest request);
}

public sealed record TripTimelineRequest(
    Trip Trip,
    IReadOnlyList<TripStop> Stops,
    IReadOnlyList<DriverAssignment> Assignments,
    IReadOnlyList<PickupScan> PickupScans,
    IReadOnlyList<OrderHandover> Handovers,
    IReadOnlyDictionary<Guid, string> DriverNameById);

public static class TripTimelineEventTypes
{
    public const string TripCreated = "trip_created";
    public const string DriverAssigned = "driver_assigned";
    public const string TripStarted = "trip_started";
    public const string PickupConfirmed = "pickup_confirmed";
    public const string StopArrived = "stop_arrived";
    public const string StopPickedUp = "stop_picked_up";
    public const string StopDelivered = "stop_delivered";
    public const string StopFailed = "stop_failed";
    public const string StopReturned = "stop_returned";
}

public sealed class TripTimelineBuilder : ITripTimelineBuilder
{
    public IReadOnlyList<TripTimelineEventDto> Build(TripTimelineRequest request)
    {
        var trip = request.Trip;
        var stopsById = request.Stops.Where(s => !s.IsDeleted).ToDictionary(s => s.Id);

        var events = new List<TripTimelineEventDto>();

        if (trip.CreatedAtUtc != default)
        {
            events.Add(new(
                $"trip:{trip.Id:N}:created",
                TripTimelineEventTypes.TripCreated,
                trip.CreatedAtUtc,
                ActorName: null,
                StopId: null,
                Note: null));
        }

        // One event per real assignment; reassignments remain visible history.
        // Rows recorded for the same driver at the same instant collapse to one event.
        var assignments = request.Assignments
            .Where(a => !a.IsDeleted)
            .OrderBy(a => a.AssignedAtUtc)
            .ThenBy(a => a.Id)
            .ToList();
        var seenAssignments = new HashSet<(Guid Driver, DateTime AtUtc)>();
        foreach (var assignment in assignments)
        {
            if (!seenAssignments.Add((assignment.DriverProfileId, assignment.AssignedAtUtc)))
            {
                continue;
            }

            var driverName = request.DriverNameById.TryGetValue(assignment.DriverProfileId, out var name) ? name : null;
            events.Add(new(
                $"trip:{trip.Id:N}:assigned:{assignment.Id:N}",
                TripTimelineEventTypes.DriverAssigned,
                assignment.AssignedAtUtc,
                ActorName: string.IsNullOrWhiteSpace(driverName) ? null : driverName,
                StopId: null,
                Note: null));
        }

        if (trip.StartedAtUtc.HasValue)
        {
            events.Add(new(
                $"trip:{trip.Id:N}:started",
                TripTimelineEventTypes.TripStarted,
                trip.StartedAtUtc.Value,
                ActorName: null,
                StopId: null,
                Note: null));
        }

        // Trip-level pickup confirmation comes from the earliest persisted scan.
        var firstScan = request.PickupScans
            .Where(s => s.TripId == trip.Id)
            .OrderBy(s => s.ScannedAtUtc)
            .ThenBy(s => s.Id)
            .FirstOrDefault();
        if (firstScan is not null)
        {
            events.Add(new(
                $"trip:{trip.Id:N}:pickup:{firstScan.Id:N}",
                TripTimelineEventTypes.PickupConfirmed,
                firstScan.ScannedAtUtc,
                ActorName: request.DriverNameById.TryGetValue(firstScan.DriverProfileId, out var scanDriver) && !string.IsNullOrWhiteSpace(scanDriver)
                    ? scanDriver
                    : null,
                StopId: null,
                Note: null));
        }

        // Per-stop pickup evidence: the completed, non-revoked handover of that stop.
        var handoverByStop = request.Handovers
            .Where(h => h.TripId == trip.Id && h.TripStopId.HasValue && h.HandedOverAtUtc.HasValue && h.RevokedAtUtc == null)
            .GroupBy(h => h.TripStopId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(h => h.HandedOverAtUtc).First());

        foreach (var stop in stopsById.Values.OrderBy(s => s.SequenceNumber).ThenBy(s => s.Id))
        {
            if (handoverByStop.TryGetValue(stop.Id, out var handover))
            {
                string? handoverDriver = handover.DriverProfileId.HasValue
                    && request.DriverNameById.TryGetValue(handover.DriverProfileId.Value, out var hd)
                    && !string.IsNullOrWhiteSpace(hd) ? hd : null;
                events.Add(new(
                    $"trip:{trip.Id:N}:stop:{stop.Id:N}:pickup",
                    TripTimelineEventTypes.StopPickedUp,
                    handover.HandedOverAtUtc!.Value,
                    ActorName: handoverDriver,
                    StopId: stop.Id.ToString(),
                    Note: null));
            }

            var arrivedAt = stop.ArrivedAtUtc ?? stop.StartedDeliveryAtUtc;
            if (arrivedAt.HasValue)
            {
                events.Add(new(
                    $"trip:{trip.Id:N}:stop:{stop.Id:N}:arrived",
                    TripTimelineEventTypes.StopArrived,
                    arrivedAt.Value,
                    ActorName: null,
                    StopId: stop.Id.ToString(),
                    Note: null));
            }

            if (stop.Status == TripStopStatus.Delivered && stop.DeliveredAtUtc.HasValue)
            {
                events.Add(new(
                    $"trip:{trip.Id:N}:stop:{stop.Id:N}:delivered",
                    TripTimelineEventTypes.StopDelivered,
                    stop.DeliveredAtUtc.Value,
                    ActorName: null,
                    StopId: stop.Id.ToString(),
                    Note: null));
            }

            // Failed/returned stops keep their real failure reason; the record's
            // last update is the persisted transition evidence for the time.
            if (stop.Status == TripStopStatus.Failed && stop.ModifiedAtUtc.HasValue)
            {
                events.Add(new(
                    $"trip:{trip.Id:N}:stop:{stop.Id:N}:failed",
                    TripTimelineEventTypes.StopFailed,
                    stop.ModifiedAtUtc.Value,
                    ActorName: null,
                    StopId: stop.Id.ToString(),
                    Note: new(stop.FailureReason ?? string.Empty, stop.FailureReason ?? string.Empty)));
            }
            else if (stop.Status == TripStopStatus.ReturnedToKitchen && stop.ModifiedAtUtc.HasValue)
            {
                events.Add(new(
                    $"trip:{trip.Id:N}:stop:{stop.Id:N}:returned",
                    TripTimelineEventTypes.StopReturned,
                    stop.ModifiedAtUtc.Value,
                    ActorName: null,
                    StopId: stop.Id.ToString(),
                    Note: new(stop.FailureReason ?? string.Empty, stop.FailureReason ?? string.Empty)));
            }
        }

        return events
            .OrderBy(e => e.OccurredAtUtc)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
    }
}
