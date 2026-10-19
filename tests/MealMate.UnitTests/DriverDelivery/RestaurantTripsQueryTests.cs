using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.DriverDelivery.Trips;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

/// <summary>
/// Restaurant trip workspace queries: only the signed-in restaurant's own trips
/// are readable, candidates come from real persisted daily orders, and the
/// previously fabricated dispatcher/plate/slot values no longer exist.
/// </summary>
public sealed class RestaurantTripsQueryTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly AppDbContext _db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Substitute.For<IDomainEventDispatcher>());

    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ITripRouteBuilder _routeBuilder = Substitute.For<ITripRouteBuilder>();

    public RestaurantTripsQueryTests()
    {
        _clock.UtcNow.Returns(Now);
    }

    private RestaurantProfile SeedRestaurant(Guid? userId = null)
    {
        var profile = new RestaurantProfile
        {
            UserId = userId ?? Guid.NewGuid(),
            LegalNameAr = "مطعم",
            LegalNameEn = "Restaurant",
            TradeNameAr = "مطعم",
            TradeNameEn = "Restaurant",
            Latitude = 29.3758m,
            Longitude = 47.9774m
        };
        _db.RestaurantProfiles.Add(profile);
        return profile;
    }

    private async Task<Trip> SeedTripAsync(Guid restaurantId, Guid? dispatcherUserId = null)
    {
        var trip = new Trip
        {
            RestaurantId = restaurantId,
            Code = "TRP-0001",
            Status = TripStatus.InProgress,
            PlannedDate = DateOnly.FromDateTime(Now),
            DispatcherUserId = dispatcherUserId,
            StartedAtUtc = Now.AddMinutes(-30),
            CreatedAtUtc = Now.AddHours(-2)
        };
        _db.Trips.Add(trip);

        _db.TripStops.Add(new TripStop
        {
            TripId = trip.Id,
            OrderCode = "BOX-0001",
            CustomerMaskedId = "CUST-0001",
            ZoneNameAr = "حولي",
            ZoneNameEn = "Hawally",
            MealSummaryAr = "وجبتان",
            MealSummaryEn = "2 meals",
            BoxCount = 2,
            SlotLabel = "12:00-14:00",
            SequenceNumber = 1,
            Status = TripStopStatus.Pending,
            Latitude = 29.33m,
            Longitude = 48.07m,
            CreatedAtUtc = Now.AddHours(-2)
        });

        await _db.SaveChangesAsync();
        return trip;
    }

    private GetRestaurantTripsQueryHandler CreateListHandler()
    {
        _user.IsAuthenticated.Returns(true);
        return new GetRestaurantTripsQueryHandler(_db, _user);
    }

    private GetRestaurantTripDetailQueryHandler CreateDetailHandler()
    {
        _user.IsAuthenticated.Returns(true);
        return new GetRestaurantTripDetailQueryHandler(_db, _user, _clock, _routeBuilder, new TripTimelineBuilder());
    }

    [Fact]
    public async Task List_ResolvesOwnRestaurant_FromSignedInUser_NotFromRequest()
    {
        var mine = SeedRestaurant();
        var other = SeedRestaurant(Guid.NewGuid());
        await SeedTripAsync(other.Id);
        await SeedTripAsync(mine.Id);
        _user.UserId.Returns(mine.UserId);

        var result = await CreateListHandler().Handle(new GetRestaurantTripsQuery(Guid.Empty), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Trips.Should().HaveCount(1);
        result.Value.Trips[0].Stops.Should().HaveCount(1);
    }

    [Fact]
    public async Task List_CallerWithoutRestaurantProfile_GetsEmptyWorkspace()
    {
        SeedRestaurant(Guid.NewGuid());
        _user.UserId.Returns(Guid.NewGuid());

        var result = await CreateListHandler().Handle(new GetRestaurantTripsQuery(Guid.Empty), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Trips.Should().BeEmpty();
        result.Value.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task List_NoLongerFabricatesDispatcherPlateOrShiftValues()
    {
        var profile = SeedRestaurant();
        var trip = await SeedTripAsync(profile.Id);
        _user.UserId.Returns(profile.UserId);

        var result = await CreateListHandler().Handle(new GetRestaurantTripsQuery(Guid.Empty), CancellationToken.None);

        var dto = result.Value.Trips.Single();
        dto.DispatcherId.Should().BeNull("the trip has no persisted dispatcher");
        dto.DispatcherName.Should().BeNull();
        dto.DriverCode.Should().BeNull("no driver is assigned and no plate fallback is allowed");
        dto.DriverName.Should().BeNull();
    }

    [Fact]
    public async Task List_DispatcherId_UsesPersistedValueWhenPresent()
    {
        var profile = SeedRestaurant();
        var dispatcherId = Guid.NewGuid();
        await SeedTripAsync(profile.Id, dispatcherId);
        _user.UserId.Returns(profile.UserId);

        var result = await CreateListHandler().Handle(new GetRestaurantTripsQuery(Guid.Empty), CancellationToken.None);

        result.Value.Trips.Single().DispatcherId.Should().Be(dispatcherId.ToString());
    }

    [Fact]
    public async Task List_Candidates_ProjectRealRegionAddressAndNullableUnknowns()
    {
        var profile = SeedRestaurant();
        _user.UserId.Returns(profile.UserId);

        var region = new Region { Code = "HAW-4", NameAr = "حولي", NameEn = "Hawally", Type = RegionType.Area, IsActive = true };
        var address = new CustomerAddress
        {
            Label = "المنزل",
            AddressLine = "قطعة 4، شارع 10",
            Latitude = 29.34m,
            Longitude = 48.05m
        };
        var subscription = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            DurationDays = 6,
            StartDate = DateOnly.FromDateTime(Now),
            EndDate = DateOnly.FromDateTime(Now).AddDays(5),
            Status = SubscriptionStatus.Active
        };
        var day = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            ServiceDate = DateOnly.FromDateTime(Now),
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            DeliveryRegionId = region.Id,
            DeliveryAddressId = address.Id,
            DeliveryAddressLine = address.AddressLine
        };
        var order = new RestaurantDailyOrder
        {
            RestaurantId = profile.Id,
            SubscriptionDayId = day.Id,
            ServiceDate = day.ServiceDate,
            Status = OrderStatus.ReadyForPickup,
            BoxCount = 3,
            CreatedAtUtc = Now.AddMinutes(-10)
        };
        _db.AddRange(region, address, subscription, day, order);
        await _db.SaveChangesAsync();

        var result = await CreateListHandler().Handle(new GetRestaurantTripsQuery(Guid.Empty), CancellationToken.None);

        var candidate = result.Value.Candidates.Single();
        candidate.Id.Should().Be(order.Id.ToString());
        candidate.ZoneLabel!.Ar.Should().Be("حولي");
        candidate.ZoneLabel.En.Should().Be("Hawally");
        candidate.Address!.Ar.Should().Be("قطعة 4، شارع 10");
        candidate.Coordinates.Should().Be(new GeoPointDto(29.34, 48.05));
        candidate.CustomerMaskedId.Should().StartWith("CUST-**");
        candidate.BoxCount.Should().Be(3);
        candidate.OrderCode.Should().BeNull("the restaurant domain carries no order code");
        candidate.MealSummary.Should().BeNull();
        candidate.SlotLabel.Should().BeNull();
        candidate.ShiftLabel.Should().BeNull();
    }

    [Fact]
    public async Task Detail_ForTripOfAnotherRestaurant_ReturnsNotFound()
    {
        var mine = SeedRestaurant();
        var other = SeedRestaurant(Guid.NewGuid());
        var foreignTrip = await SeedTripAsync(other.Id);
        _user.UserId.Returns(mine.UserId);

        var result = await CreateDetailHandler().Handle(
            new GetRestaurantTripDetailQuery(foreignTrip.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task Detail_UnknownTrip_ReturnsNotFound()
    {
        var mine = SeedRestaurant();
        _user.UserId.Returns(mine.UserId);

        var result = await CreateDetailHandler().Handle(
            new GetRestaurantTripDetailQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task Detail_EmitsRouteAndTimelineFromPersistedEvidence()
    {
        var profile = SeedRestaurant();
        var trip = await SeedTripAsync(profile.Id);
        _user.UserId.Returns(profile.UserId);

        var stop = await _db.TripStops.SingleAsync(s => s.TripId == trip.Id);
        _db.OrderHandovers.Add(new OrderHandover
        {
            DailyOrderId = Guid.NewGuid(),
            TripId = trip.Id,
            TripStopId = stop.Id,
            DriverProfileId = Guid.NewGuid(),
            AttemptNumber = 1,
            HandedOverAtUtc = Now.AddMinutes(-20),
            Status = "Completed",
            CreatedAtUtc = Now.AddMinutes(-20)
        });
        await _db.SaveChangesAsync();

        var route = new TripRouteDto(
            TripRouteBuilder.StatusReady,
            new GeoPointDto(29.3758, 47.9774),
            new GeoPointDto(29.33, 48.07),
            DriverLocation: null,
            EncodedPolyline: "abc",
            PolylineEncoding: TripRouteBuilder.PolylineEncodingGoogle5,
            DistanceMeters: 5200,
            DurationSeconds: 900,
            LastLocationAtUtc: null,
            UnavailableReason: null);
        _routeBuilder
            .BuildAsync(Arg.Any<TripRouteRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TripRouteResult(
                route,
                new Dictionary<Guid, StopRouteEvidence> { [stop.Id] = new(Now.AddMinutes(15), 5200) }));

        var result = await CreateDetailHandler().Handle(
            new GetRestaurantTripDetailQuery(trip.Id), CancellationToken.None);

        var dto = result.Value!;
        dto.Route.Should().NotBeNull();
        dto.Route!.Status.Should().Be(TripRouteBuilder.StatusReady);
        dto.Route.EncodedPolyline.Should().Be("abc");

        var dtoStop = dto.Stops.Single();
        dtoStop.EtaUtc.Should().Be(Now.AddMinutes(15));
        dtoStop.DistanceMeters.Should().Be(5200);
        dtoStop.PickedUpAtUtc.Should().Be(Now.AddMinutes(-20));

        dto.Timeline.Should().NotBeNull();
        dto.Timeline!.Select(e => e.Type).Should().Contain(TripTimelineEventTypes.TripCreated);
        dto.Timeline!.Select(e => e.Type).Should().Contain(TripTimelineEventTypes.TripStarted);
        dto.Timeline!.Select(e => e.Type).Should().Contain(TripTimelineEventTypes.StopPickedUp);
    }
}
