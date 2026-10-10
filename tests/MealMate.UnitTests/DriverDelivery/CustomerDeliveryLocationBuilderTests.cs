using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.CustomerTracking;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.DriverDelivery;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

/// <summary>
/// Location/ETA truthfulness: only recorded fixes are reported, freshness follows the
/// shared navigation policy, the shift-start coordinate is never a live fallback, and an
/// ETA exists only from real routing or confirmed arrival — never from straight-line
/// distance and never for terminal boxes or stops with earlier pending deliveries.
/// </summary>
public sealed class CustomerDeliveryLocationBuilderTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly AppDbContext _db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Substitute.For<IDomainEventDispatcher>());

    private readonly IGoogleRoutingService _routing = Substitute.For<IGoogleRoutingService>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    private CustomerDeliveryLocationBuilder CreateBuilder()
    {
        _clock.UtcNow.Returns(Now);
        return new CustomerDeliveryLocationBuilder(
            _db,
            _routing,
            new MemoryCache(new MemoryCacheOptions()),
            _clock,
            Options.Create(new DriverNavigationOptions()));
    }

    private static CustomerDeliveryLocationRequest Request(
        bool isArrived = false,
        bool isTerminal = false,
        bool earlierStops = false)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            isArrived,
            isTerminal,
            earlierStops,
            DestinationLatitude: 29.33m,
            DestinationLongitude: 48.07m);

    [Fact]
    public async Task No_fix_reports_nothing_and_never_falls_back_to_shift_start()
    {
        var builder = CreateBuilder();

        var result = await builder.BuildAsync(Request(), CancellationToken.None);

        result.Latitude.Should().BeNull();
        result.Longitude.Should().BeNull();
        result.IsLive.Should().BeFalse();
        result.LocationUnavailableReason.Should().Be(CustomerDeliveryLocationBuilder.ReasonDriverMissing);
        result.EtaMinutes.Should().BeNull();
        result.EtaSource.Should().BeNull();
        await _routing.DidNotReceiveWithAnyArgs().GetDirectionsAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task Fresh_valid_fix_is_live_with_captured_and_received_times()
    {
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        await SeedDriverFixAsync(driverId, tripId, Now.AddSeconds(-30));
        var builder = CreateBuilder();

        var result = await builder.BuildAsync(
            Request() with { DriverProfileId = driverId, TripId = tripId }, CancellationToken.None);

        result.Latitude.Should().Be(29.335d);
        result.Longitude.Should().Be(48.075d);
        result.CapturedAtUtc.Should().Be(Now.AddSeconds(-30));
        result.ReceivedAtUtc.Should().Be(Now.AddSeconds(-29));
        result.IsLive.Should().BeTrue();
        result.LocationUnavailableReason.Should().BeNull();
    }

    [Fact]
    public async Task Stale_fix_stays_visible_but_never_live_and_never_produces_an_eta()
    {
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        await SeedDriverFixAsync(driverId, tripId, Now.AddMinutes(-10));
        RoutingReturns(minutes: 8);
        var builder = CreateBuilder();

        var result = await builder.BuildAsync(
            Request() with { DriverProfileId = driverId, TripId = tripId }, CancellationToken.None);

        result.Latitude.Should().NotBeNull();
        result.IsLive.Should().BeFalse();
        result.LocationUnavailableReason.Should().Be(CustomerDeliveryLocationBuilder.ReasonLocationStale);
        result.EtaMinutes.Should().BeNull();
    }

    [Fact]
    public async Task Out_of_bounds_fix_is_invalid_and_never_used()
    {
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        _db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
        {
            TripId = tripId,
            DriverProfileId = driverId,
            Latitude = 55m,
            Longitude = 100m,
            RecordedAtUtc = Now.AddSeconds(-10)
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var builder = CreateBuilder();

        var result = await builder.BuildAsync(
            Request() with { DriverProfileId = driverId, TripId = tripId }, CancellationToken.None);

        result.Latitude.Should().BeNull();
        result.LocationUnavailableReason.Should().Be(CustomerDeliveryLocationBuilder.ReasonLocationInvalid);
    }

    [Fact]
    public async Task Future_timestamped_fix_is_a_broken_fix_not_a_location()
    {
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        await SeedDriverFixAsync(driverId, tripId, Now.AddMinutes(10));
        var builder = CreateBuilder();

        var result = await builder.BuildAsync(
            Request() with { DriverProfileId = driverId, TripId = tripId }, CancellationToken.None);

        result.Latitude.Should().BeNull();
        result.LocationUnavailableReason.Should().Be(CustomerDeliveryLocationBuilder.ReasonLocationInvalid);
    }

    [Fact]
    public async Task Fix_of_another_trip_or_driver_is_not_used_for_this_box()
    {
        await SeedDriverFixAsync(Guid.NewGuid(), Guid.NewGuid(), Now.AddSeconds(-30));
        var builder = CreateBuilder();

        // The requested driver/trip has no fix of its own — the other trip's fix is irrelevant.
        var result = await builder.BuildAsync(Request(), CancellationToken.None);

        result.Latitude.Should().BeNull();
        result.LocationUnavailableReason.Should().Be(CustomerDeliveryLocationBuilder.ReasonDriverMissing);
    }

    [Fact]
    public async Task Arrival_reports_zero_eta_backed_by_arrival_evidence()
    {
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        await SeedDriverFixAsync(driverId, tripId, Now.AddSeconds(-30));
        var builder = CreateBuilder();

        var result = await builder.BuildAsync(
            Request(isArrived: true) with { DriverProfileId = driverId, TripId = tripId }, CancellationToken.None);

        result.EtaMinutes.Should().Be(0);
        result.EtaSource.Should().Be(CustomerDeliveryEtaSources.Arrival);
        await _routing.DidNotReceiveWithAnyArgs().GetDirectionsAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task Terminal_boxes_expose_no_driver_coordinates_and_no_eta()
    {
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        await SeedDriverFixAsync(driverId, tripId, Now.AddSeconds(-30));
        var builder = CreateBuilder();

        var result = await builder.BuildAsync(
            Request(isTerminal: true) with { DriverProfileId = driverId, TripId = tripId }, CancellationToken.None);

        result.Latitude.Should().BeNull();
        result.Longitude.Should().BeNull();
        result.EtaMinutes.Should().BeNull();
        result.IsLive.Should().BeFalse();
        await _routing.DidNotReceiveWithAnyArgs().GetDirectionsAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task Earlier_undelivered_stops_block_a_direct_route_eta()
    {
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        await SeedDriverFixAsync(driverId, tripId, Now.AddSeconds(-30));
        RoutingReturns(minutes: 8);
        var builder = CreateBuilder();

        var result = await builder.BuildAsync(
            Request(earlierStops: true) with { DriverProfileId = driverId, TripId = tripId }, CancellationToken.None);

        result.IsLive.Should().BeTrue();
        result.EtaMinutes.Should().BeNull();
        result.EtaSource.Should().BeNull();
    }

    [Fact]
    public async Task Routing_outage_yields_null_eta_not_a_straight_line_estimate()
    {
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        await SeedDriverFixAsync(driverId, tripId, Now.AddSeconds(-30));
        _routing.GetDirectionsAsync(Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<RouteDirectionsResult>(Error.Failure("routing.down", "provider down")));
        var builder = CreateBuilder();

        var result = await builder.BuildAsync(
            Request() with { DriverProfileId = driverId, TripId = tripId }, CancellationToken.None);

        result.EtaMinutes.Should().BeNull();
        result.EtaSource.Should().BeNull();
    }

    [Fact]
    public async Task Successful_real_route_becomes_a_road_route_eta()
    {
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        await SeedDriverFixAsync(driverId, tripId, Now.AddSeconds(-30));
        RoutingReturns(minutes: 8);
        var builder = CreateBuilder();

        var result = await builder.BuildAsync(
            Request() with { DriverProfileId = driverId, TripId = tripId }, CancellationToken.None);

        result.EtaMinutes.Should().Be(8);
        result.EtaSource.Should().Be(CustomerDeliveryEtaSources.RoadRoute);
    }

    [Fact]
    public async Task Fallback_estimates_from_the_provider_are_never_advertised()
    {
        var driverId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        await SeedDriverFixAsync(driverId, tripId, Now.AddSeconds(-30));
        _routing.GetDirectionsAsync(Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(new RouteDirectionsResult(
                DistanceMeters: 4000, DistanceKm: 4, DurationSeconds: 480, DurationMinutes: 8,
                OverviewPolyline: null, IsFallbackEstimate: true,
                OriginLatitude: 29m, OriginLongitude: 48m, DestinationLatitude: 29.33m, DestinationLongitude: 48.07m));
        var builder = CreateBuilder();

        var result = await builder.BuildAsync(
            Request() with { DriverProfileId = driverId, TripId = tripId }, CancellationToken.None);

        result.EtaMinutes.Should().BeNull();
        result.EtaSource.Should().BeNull();
    }

    private async Task SeedDriverFixAsync(Guid driverId, Guid tripId, DateTime recordedAtUtc)
    {
        _db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
        {
            TripId = tripId,
            DriverProfileId = driverId,
            Latitude = 29.335m,
            Longitude = 48.075m,
            RecordedAtUtc = recordedAtUtc,
            CapturedAtUtc = recordedAtUtc,
            ReceivedAtUtc = recordedAtUtc.AddSeconds(1)
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private void RoutingReturns(int minutes)
    {
        _routing.GetDirectionsAsync(Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(new RouteDirectionsResult(
                DistanceMeters: 4000, DistanceKm: 4, DurationSeconds: minutes * 60, DurationMinutes: minutes,
                OverviewPolyline: "abc{de", IsFallbackEstimate: false,
                OriginLatitude: 29m, OriginLongitude: 48m, DestinationLatitude: 29.33m, DestinationLongitude: 48.07m));
    }
}
