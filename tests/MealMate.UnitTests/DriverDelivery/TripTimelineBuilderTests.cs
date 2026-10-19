using FluentAssertions;
using MealMate.Application.Features.DriverDelivery.Trips;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

/// <summary>
/// The restaurant trip timeline: every event traces one persisted timestamp —
/// no event is inferred from the current status, revoked handovers are not a
/// pickup, and failed/returned stops keep their real transition evidence.
/// </summary>
public sealed class TripTimelineBuilderTests
{
    private static readonly DateTime Base = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    private static Trip NewTrip()
        => new()
        {
            Code = "TRP-9001",
            Status = TripStatus.InProgress,
            CreatedAtUtc = Base,
            StartedAtUtc = Base.AddMinutes(10)
        };

    private static TripStop NewStop(int sequence, TripStopStatus status = TripStopStatus.Pending)
        => new()
        {
            TripId = Guid.NewGuid(),
            OrderCode = "BOX-1",
            CustomerMaskedId = "CUST-0001",
            ZoneNameAr = "منطقة",
            ZoneNameEn = "Zone",
            FullAddress = null,
            MealSummaryAr = "وجبـة",
            MealSummaryEn = "meal",
            BoxCount = 1,
            SlotLabel = "12:00-14:00",
            SequenceNumber = sequence,
            Status = status,
            CreatedAtUtc = Base
        };

    [Fact]
    public void Build_EmitsOrderedEventsFromRealTimestamps()
    {
        var trip = NewTrip();
        var stop = NewStop(1);
        stop.ArrivedAtUtc = Base.AddMinutes(20);
        stop.Status = TripStopStatus.Delivered;
        stop.DeliveredAtUtc = Base.AddMinutes(40);

        var driverId = Guid.NewGuid();
        var assignment = new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driverId,
            AssignedAtUtc = Base.AddMinutes(5),
            Status = "Active",
            CreatedAtUtc = Base.AddMinutes(5)
        };
        var scan = new PickupScan
        {
            TripId = trip.Id,
            DriverProfileId = driverId,
            BarcodeValue = "BOX-1",
            ScannedAtUtc = Base.AddMinutes(12),
            CreatedAtUtc = Base.AddMinutes(12)
        };
        var handover = new OrderHandover
        {
            DailyOrderId = Guid.NewGuid(),
            TripId = trip.Id,
            TripStopId = stop.Id,
            DriverProfileId = driverId,
            AttemptNumber = 1,
            HandedOverAtUtc = Base.AddMinutes(15),
            Status = "Completed",
            CreatedAtUtc = Base.AddMinutes(15)
        };

        var events = new TripTimelineBuilder().Build(new TripTimelineRequest(
            trip,
            [stop],
            [assignment],
            [scan],
            [handover],
            new Dictionary<Guid, string> { [driverId] = "Sami" }));

        events.Select(e => e.Type).Should().Equal(
            TripTimelineEventTypes.TripCreated,
            TripTimelineEventTypes.DriverAssigned,
            TripTimelineEventTypes.TripStarted,
            TripTimelineEventTypes.PickupConfirmed,
            TripTimelineEventTypes.StopPickedUp,
            TripTimelineEventTypes.StopArrived,
            TripTimelineEventTypes.StopDelivered);

        events[0].OccurredAtUtc.Should().Be(Base);
        events[1].OccurredAtUtc.Should().Be(Base.AddMinutes(5));
        events[1].ActorName.Should().Be("Sami");
        events[3].OccurredAtUtc.Should().Be(Base.AddMinutes(12));
        events[4].StopId.Should().Be(stop.Id.ToString());
        events[4].OccurredAtUtc.Should().Be(Base.AddMinutes(15));
        events[5].OccurredAtUtc.Should().Be(Base.AddMinutes(20));
        events[6].OccurredAtUtc.Should().Be(Base.AddMinutes(40));
    }

    [Fact]
    public void Build_IgnoresLegacyArrivalFallback_WhenNoArrivalEvidenceExists()
    {
        var trip = NewTrip();
        var stop = NewStop(1);
        stop.Status = TripStopStatus.Pending;

        var events = new TripTimelineBuilder().Build(new TripTimelineRequest(
            trip,
            [stop],
            [],
            [],
            [],
            new Dictionary<Guid, string>()));

        events.Select(e => e.Type).Should().NotContain(TripTimelineEventTypes.StopArrived);
    }

    [Fact]
    public void Build_UsesLegacyStartedDeliveryAsArrivalWhenArrivedAtUtcMissing()
    {
        var trip = NewTrip();
        var stop = NewStop(1);
        stop.StartedDeliveryAtUtc = Base.AddMinutes(25);
        stop.Status = TripStopStatus.PickedUp;

        var events = new TripTimelineBuilder().Build(new TripTimelineRequest(
            trip,
            [stop],
            [],
            [],
            [],
            new Dictionary<Guid, string>()));

        events.Single(e => e.Type == TripTimelineEventTypes.StopArrived).OccurredAtUtc
            .Should().Be(Base.AddMinutes(25));
    }

    [Fact]
    public void Build_SkipsRevokedHandovers_AsPickupEvidence()
    {
        var trip = NewTrip();
        var stop = NewStop(1);
        stop.Status = TripStopStatus.PickedUp;
        var revoked = new OrderHandover
        {
            DailyOrderId = Guid.NewGuid(),
            TripId = trip.Id,
            TripStopId = stop.Id,
            DriverProfileId = Guid.NewGuid(),
            AttemptNumber = 1,
            HandedOverAtUtc = Base.AddMinutes(15),
            Status = "Completed",
            RevokedAtUtc = Base.AddMinutes(30),
            CreatedAtUtc = Base.AddMinutes(15)
        };

        var events = new TripTimelineBuilder().Build(new TripTimelineRequest(
            trip,
            [stop],
            [],
            [],
            [revoked],
            new Dictionary<Guid, string>()));

        events.Select(e => e.Type).Should().NotContain(TripTimelineEventTypes.StopPickedUp);
    }

    [Fact]
    public void Build_CollapsesDuplicateAssignmentRowsForSameDriverAndInstant()
    {
        var trip = NewTrip();
        var driverId = Guid.NewGuid();
        var first = new DriverAssignment { TripId = trip.Id, DriverProfileId = driverId, AssignedAtUtc = Base.AddMinutes(5), Status = "Assigned", CreatedAtUtc = Base };
        var duplicate = new DriverAssignment { TripId = trip.Id, DriverProfileId = driverId, AssignedAtUtc = Base.AddMinutes(5), Status = "Active", CreatedAtUtc = Base };
        var reassignment = new DriverAssignment { TripId = trip.Id, DriverProfileId = driverId, AssignedAtUtc = Base.AddMinutes(50), Status = "Reassigned", CreatedAtUtc = Base };

        var events = new TripTimelineBuilder().Build(new TripTimelineRequest(
            trip,
            [],
            [first, duplicate, reassignment],
            [],
            [],
            new Dictionary<Guid, string>()));

        events.Where(e => e.Type == TripTimelineEventTypes.DriverAssigned)
            .Should().HaveCount(2);
    }

    [Fact]
    public void Build_FailedStop_KeepsRealFailureReasonAndTransitionTime()
    {
        var trip = NewTrip();
        var stop = NewStop(1, TripStopStatus.Failed);
        stop.FailureReason = "customer unavailable";
        stop.ModifiedAtUtc = Base.AddMinutes(45);

        var events = new TripTimelineBuilder().Build(new TripTimelineRequest(
            trip,
            [stop],
            [],
            [],
            [],
            new Dictionary<Guid, string>()));

        var failure = events.Should().ContainSingle(e => e.Type == TripTimelineEventTypes.StopFailed).Which;
        failure.OccurredAtUtc.Should().Be(Base.AddMinutes(45));
        failure.Note.Should().NotBeNull();
        failure.Note!.Ar.Should().Be("customer unavailable");
        failure.StopId.Should().Be(stop.Id.ToString());
    }

    [Fact]
    public void Build_ReturnedStop_EmitsReturnedEvent()
    {
        var trip = NewTrip();
        var stop = NewStop(1, TripStopStatus.ReturnedToKitchen);
        stop.ModifiedAtUtc = Base.AddMinutes(60);

        var events = new TripTimelineBuilder().Build(new TripTimelineRequest(
            trip,
            [stop],
            [],
            [],
            [],
            new Dictionary<Guid, string>()));

        events.Should().ContainSingle(e => e.Type == TripTimelineEventTypes.StopReturned);
    }

    [Fact]
    public void Build_MissingActorName_RendersNullActor()
    {
        var trip = NewTrip();
        var assignment = new DriverAssignment { TripId = trip.Id, DriverProfileId = Guid.NewGuid(), AssignedAtUtc = Base.AddMinutes(5), Status = "Assigned", CreatedAtUtc = Base };

        var events = new TripTimelineBuilder().Build(new TripTimelineRequest(
            trip,
            [],
            [assignment],
            [],
            [],
            new Dictionary<Guid, string>()));

        events.Single(e => e.Type == TripTimelineEventTypes.DriverAssigned)
            .ActorName.Should().BeNull();
    }
}
