using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Application.Features.DriverDelivery.Trips;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

/// <summary>
/// The restaurant trip route builder: street geometry always comes from the real
/// routing provider, per-stop evidence stays null when no real answer exists,
/// and a cancelled terminal trip never pretends to have a live route.
/// </summary>
public sealed class TripRouteBuilderTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    private const decimal RestaurantLat = 29.3758m;
    private const decimal RestaurantLng = 47.9774m;

    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public TripRouteBuilderTests()
    {
        _clock.UtcNow.Returns(Now);
    }

    private static Trip NewTrip(TripStatus status)
        => new()
        {
            Code = "TRP-9999",
            RestaurantId = Guid.NewGuid(),
            Status = status,
            PlannedDate = DateOnly.FromDateTime(Now),
            CreatedAtUtc = Now.AddDays(-1)
        };

    private static TripStop NewStop(int sequence, decimal lat, decimal lng, TripStopStatus status = TripStopStatus.Pending)
        => new()
        {
            TripId = Guid.NewGuid(),
            OrderCode = "BOX-1",
            CustomerMaskedId = "CUST-0001",
            ZoneNameAr = "منطقة",
            ZoneNameEn = "Zone",
            FullAddress = "Address",
            MealSummaryAr = "وجبات",
            MealSummaryEn = "meals",
            BoxCount = 1,
            SlotLabel = "12:00-14:00",
            SequenceNumber = sequence,
            Status = status,
            Latitude = lat,
            Longitude = lng,
            CreatedAtUtc = Now
        };

    /// <summary>Provider stub that always answers a real (non-fallback) street geometry for the requested leg.</summary>
    private static IGoogleRoutingService RouteStub()
    {
        var routing = Substitute.For<IGoogleRoutingService>();
        routing.GetDirectionsAsync(
                Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var fromLat = (double)callInfo.ArgAt<decimal>(0);
                var fromLng = (double)callInfo.ArgAt<decimal>(1);
                var toLat = (double)callInfo.ArgAt<decimal>(2);
                var toLng = (double)callInfo.ArgAt<decimal>(3);
                var leg = new[] { new GeoPointDto(fromLat, fromLng), new GeoPointDto(toLat, toLng) };
                var encoded = TripRouteBuilder.EncodePolyline(leg);
                if (encoded is null)
                {
                    return MealMate.Application.Common.Models.Result.Failure<RouteDirectionsResult>(
                        Error.Failure("trips.provider_error", "Provider call failed"));
                }

                return MealMate.Application.Common.Models.Result.Success(new RouteDirectionsResult(
                    DistanceMeters: 1000,
                    DistanceKm: 1.0,
                    DurationSeconds: 300,
                    DurationMinutes: 5,
                    OverviewPolyline: encoded,
                    IsFallbackEstimate: false,
                    OriginLatitude: (decimal)fromLat,
                    OriginLongitude: (decimal)fromLng,
                    DestinationLatitude: (decimal)toLat,
                    DestinationLongitude: (decimal)toLng));
            });
        return routing;
    }

    private async Task<TripRouteResult> BuildAsync(
        IGoogleRoutingService routing,
        Trip trip,
        params TripStop[] stops)
    {
        var builder = new TripRouteBuilder(
            routing,
            _clock,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new DriverNavigationOptions()));
        return await builder.BuildAsync(
            new TripRouteRequest(trip, stops, RestaurantLat, RestaurantLng, null, DriverLocationStale: false),
            CancellationToken.None);
    }

    [Fact]
    public void DecodePolyline_OfficialSample_ReturnsKnownPoints()
    {
        var points = TripRouteBuilder.DecodePolyline("_p~iF~ps|U_ulLnnqC_mqNvxq`@");

        points.Should().BeEquivalentTo<GeoPointDto>(new[]
        {
            new GeoPointDto(38.5, -120.2),
            new GeoPointDto(40.7, -120.95),
            new GeoPointDto(43.252, -126.453)
        });
    }

    [Fact]
    public void EncodeDecode_RoundTrip_PreservesPoints()
    {
        var original = new[]
        {
            new GeoPointDto(29.3758, 47.9774),
            new GeoPointDto(29.33, 48.07),
            new GeoPointDto(29.29, 47.99)
        };

        var encoded = TripRouteBuilder.EncodePolyline(original);
        encoded.Should().NotBeNullOrWhiteSpace();

        TripRouteBuilder.DecodePolyline(encoded!).Should().BeEquivalentTo(original);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("\u00ff")]
    public void DecodePolyline_MalformedInput_ReturnsEmpty(string encoded)
    {
        TripRouteBuilder.DecodePolyline(encoded).Should().BeEmpty();
    }

    [Fact]
    public void ConcatenatePolyline_DeduplicatesJoinPoint_AndFailsOnMalformedLeg()
    {
        var leg1 = TripRouteBuilder.EncodePolyline([new GeoPointDto(29.3758, 47.9774), new GeoPointDto(29.33, 48.07)])!;
        var leg2 = TripRouteBuilder.EncodePolyline([new GeoPointDto(29.33, 48.07), new GeoPointDto(29.29, 47.99)])!;

        var joined = TripRouteBuilder.ConcatenatePolyline([leg1, leg2]);
        joined.Should().HaveCount(3);

        TripRouteBuilder.ConcatenatePolyline([leg1, "??bad"]).Should().BeEmpty();
    }

    [Fact]
    public async Task PlannedTrip_WithRealLegs_ReturnsReadyRouteWithTotalsAndEvidence()
    {
        var stop1 = NewStop(1, 29.33m, 48.07m);
        var stop2 = NewStop(2, 29.29m, 47.99m);
        var trip = NewTrip(TripStatus.Assigned);

        var result = await BuildAsync(RouteStub(), trip, stop1, stop2);

        result.Route.Status.Should().Be(TripRouteBuilder.StatusReady);
        result.Route.EncodedPolyline.Should().NotBeNullOrWhiteSpace();
        result.Route.PolylineEncoding.Should().Be(TripRouteBuilder.PolylineEncodingGoogle5);
        result.Route.DistanceMeters.Should().Be(2000);
        result.Route.DurationSeconds.Should().Be(600);
        result.Route.Origin.Should().Be(new GeoPointDto(29.3758, 47.9774));
        result.Route.Destination.Should().Be(new GeoPointDto(29.29, 47.99));
        result.Route.UnavailableReason.Should().BeNull();

        result.StopEvidence[stop1.Id].DistanceMeters.Should().Be(1000);
        result.StopEvidence[stop1.Id].EtaUtc.Should().Be(Now.AddSeconds(300));
        result.StopEvidence[stop2.Id].DistanceMeters.Should().Be(2000);
        result.StopEvidence[stop2.Id].EtaUtc.Should().Be(Now.AddSeconds(600));
    }

    [Fact]
    public async Task MissingRestaurantOrigin_ReturnsUnavailableWithOriginReason()
    {
        var trip = NewTrip(TripStatus.Assigned);
        var stop = NewStop(1, 29.33m, 48.07m);
        var builder = new TripRouteBuilder(
            RouteStub(),
            _clock,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new DriverNavigationOptions()));

        var result = await builder.BuildAsync(
            new TripRouteRequest(trip, [stop], null, null, null, DriverLocationStale: false),
            CancellationToken.None);

        result.Route.Status.Should().Be(TripRouteBuilder.StatusUnavailable);
        result.Route.UnavailableReason.Should().Be(TripRouteBuilder.ReasonRestaurantOriginMissing);
        result.Route.EncodedPolyline.Should().BeNull();
        result.StopEvidence[stop.Id].EtaUtc.Should().BeNull();
    }

    [Fact]
    public async Task MissingStopCoordinates_ReturnsUnavailableWithCoordinateReason()
    {
        var trip = NewTrip(TripStatus.Assigned);
        var stop = NewStop(1, 29.33m, 48.07m);
        var unplaced = NewStop(2, 0m, 500m);
        unplaced.Latitude = null;
        unplaced.Longitude = null;

        var result = await BuildAsync(RouteStub(), trip, stop, unplaced);

        result.Route.Status.Should().Be(TripRouteBuilder.StatusUnavailable);
        result.Route.UnavailableReason.Should().Be(TripRouteBuilder.ReasonStopCoordinatesMissing);
    }

    [Fact]
    public async Task ProviderFallbackEstimate_IsTreatedAsMissingGeometry()
    {
        var routing = Substitute.For<IGoogleRoutingService>();
        routing.GetDirectionsAsync(
                Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new RouteDirectionsResult(
                DistanceMeters: 5000,
                DistanceKm: 5.0,
                DurationSeconds: 1200,
                DurationMinutes: 20,
                OverviewPolyline: "estimated",
                IsFallbackEstimate: true,
                OriginLatitude: RestaurantLat,
                OriginLongitude: RestaurantLng,
                DestinationLatitude: 29.33m,
                DestinationLongitude: 48.07m)));

        var trip = NewTrip(TripStatus.Assigned);
        var stop = NewStop(1, 29.33m, 48.07m);

        var result = await BuildAsync(routing, trip, stop);

        result.Route.Status.Should().Be(TripRouteBuilder.StatusUnavailable);
        result.Route.UnavailableReason.Should().Be(TripRouteBuilder.ReasonProviderGeometryMissing);
    }

    [Fact]
    public async Task CompletedTrip_KeepsDeliveredDestination_WithoutRoute()
    {
        var delivered = NewStop(1, 29.33m, 48.07m, TripStopStatus.Delivered);
        var pending = NewStop(2, 29.29m, 47.99m);
        var trip = NewTrip(TripStatus.Completed);

        var result = await BuildAsync(RouteStub(), trip, delivered, pending);

        result.Route.Status.Should().Be(TripRouteBuilder.StatusCompleted);
        result.Route.EncodedPolyline.Should().BeNull();
        result.Route.DriverLocation.Should().BeNull();
        result.Route.Origin.Should().Be(new GeoPointDto(29.3758, 47.9774));
        result.Route.Destination.Should().Be(new GeoPointDto(29.33, 48.07));
    }

    [Fact]
    public async Task CancelledTrip_ReturnsUnavailableWithCancelReason()
    {
        var trip = NewTrip(TripStatus.Cancelled);
        var stop = NewStop(1, 29.33m, 48.07m);

        var result = await BuildAsync(RouteStub(), trip, stop);

        result.Route.Status.Should().Be(TripRouteBuilder.StatusUnavailable);
        result.Route.UnavailableReason.Should().Be(TripRouteBuilder.ReasonTripCancelled);
    }

    [Fact]
    public async Task InProgress_FreshDriverLocation_ContinuesRouteFromDriverToRemainingStops()
    {
        var delivered = NewStop(1, 29.33m, 48.07m, TripStopStatus.Delivered);
        var remaining = NewStop(2, 29.29m, 47.99m);
        var trip = NewTrip(TripStatus.InProgress);
        var driver = new GeoPointDto(29.3, 48.0);
        var tracking = new DeliveryTrackingPoint
        {
            TripId = trip.Id,
            Trip = trip,
            DriverProfileId = Guid.NewGuid(),
            Latitude = (decimal)driver.Latitude,
            Longitude = (decimal)driver.Longitude,
            RecordedAtUtc = Now.AddSeconds(-30)
        };

        var builder = new TripRouteBuilder(
            RouteStub(),
            _clock,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new DriverNavigationOptions()));

        var result = await builder.BuildAsync(
            new TripRouteRequest(trip, [delivered, remaining], RestaurantLat, RestaurantLng, tracking, DriverLocationStale: false),
            CancellationToken.None);

        result.Route.Status.Should().Be(TripRouteBuilder.StatusReady);
        result.Route.Origin.Should().Be(driver);
        result.Route.Destination.Should().Be(new GeoPointDto(29.29, 47.99));
        result.StopEvidence[remaining.Id].DistanceMeters.Should().Be(1000);
        result.StopEvidence[remaining.Id].EtaUtc.Should().Be(Now.AddSeconds(300));
        result.StopEvidence[delivered.Id].EtaUtc.Should().BeNull();
        result.StopEvidence[delivered.Id].DistanceMeters.Should().BeNull();
    }

    [Fact]
    public async Task InProgress_StaleDriverLocation_FallsBackToRestaurantOriginAndFlagsStale()
    {
        var remaining = NewStop(1, 29.29m, 47.99m);
        var trip = NewTrip(TripStatus.InProgress);
        var tracking = new DeliveryTrackingPoint
        {
            TripId = trip.Id,
            Trip = trip,
            DriverProfileId = Guid.NewGuid(),
            Latitude = 29.3m,
            Longitude = 48.0m,
            RecordedAtUtc = Now.AddSeconds(-600)
        };

        var builder = new TripRouteBuilder(
            RouteStub(),
            _clock,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new DriverNavigationOptions()));

        var result = await builder.BuildAsync(
            new TripRouteRequest(trip, [remaining], RestaurantLat, RestaurantLng, tracking, DriverLocationStale: false),
            CancellationToken.None);

        result.Route.Status.Should().Be(TripRouteBuilder.StatusStaleDriverLocation);
        result.Route.Origin.Should().Be(new GeoPointDto(29.3758, 47.9774));
        result.Route.LastLocationAtUtc.Should().Be(tracking.RecordedAtUtc);
    }
}
