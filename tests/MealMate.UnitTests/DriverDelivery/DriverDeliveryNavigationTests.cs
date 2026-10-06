using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.DriverDelivery;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

/// <summary>
/// Rules of the driver delivery-map navigation builder: origin validity
/// (fresh tracking / stale / missing / shift-start / invalid), destination
/// validity, delivered stops, and localized labels. Real routing-provider
/// cases (Ready paths, cache, Google adapter) are covered in the routing
/// sections of this class and in DriverGoogleRoutingTests.
/// </summary>
public class DriverDeliveryNavigationTests
{
    private static readonly DateTime NowUtc = new(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);
    private const decimal KuwaitLat = 29.3375m;
    private const decimal KuwaitLon = 48.0280m;

    private sealed class FixedClock(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow => utcNow;
        public DateOnly TodayUtc => DateOnly.FromDateTime(utcNow);
    }

    private static DriverDeliveryNavigationBuilder CreateBuilder(out IGoogleRoutingService routing, DateTime? now = null)
    {
        routing = Substitute.For<IGoogleRoutingService>();
        return new DriverDeliveryNavigationBuilder(
            routing,
            new MemoryCache(new MemoryCacheOptions()),
            new FixedClock(now ?? NowUtc),
            Options.Create(new DriverNavigationOptions()));
    }

    private static Result<RouteDirectionsResult> ProviderRoute(
        string polyline = "_p~iF~ps|U_ulLnnqC",
        long distanceMeters = 5200,
        long durationSeconds = 480,
        bool isFallbackEstimate = false) => Result.Success(new RouteDirectionsResult(
        DistanceMeters: distanceMeters,
        DistanceKm: distanceMeters / 1000.0,
        DurationSeconds: durationSeconds,
        DurationMinutes: (int)Math.Ceiling(durationSeconds / 60.0),
        OverviewPolyline: polyline,
        IsFallbackEstimate: isFallbackEstimate,
        OriginLatitude: KuwaitLat,
        OriginLongitude: KuwaitLon,
        DestinationLatitude: 29.3380m,
        DestinationLongitude: 48.0230m));

    private static void ConfigureProviderRoute(IGoogleRoutingService routing)
        => routing.GetDirectionsAsync(
                Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(_ => ProviderRoute());

    private static DriverMapStopDto Stop(
        string status = "Pending",
        double? latitude = 29.3380,
        double? longitude = 48.0230,
        string customerName = "محمد علي",
        Guid? stopId = null)
    {
        var id = stopId ?? Guid.NewGuid();
        return new DriverMapStopDto(
        StopId: id,
        BoxId: id,
        TripId: Guid.NewGuid(),
        SequenceBadge: "1/1",
        SequenceNumber: 1,
        BoxCode: "#BX-1256",
        CustomerName: customerName,
        CustomerPhone: string.Empty,
        AddressShort: "السالمية",
        FullAddress: "شارع الخليج، السالمية",
        MealsCount: 3,
        MealsSummary: "3 وجبات",
        DeliveryTimeSlot: "09:20 ص",
        Status: status,
        StatusText: "خارج التوصيل",
        StatusColor: "orange",
        IsCurrent: true,
        Latitude: latitude,
        Longitude: longitude);
    }

    private static DeliveryTrackingPoint Tracking(
        DateTime recordedAtUtc,
        decimal latitude = KuwaitLat,
        decimal longitude = KuwaitLon) => new()
    {
        TripId = Guid.NewGuid(),
        DriverProfileId = Guid.NewGuid(),
        Latitude = latitude,
        Longitude = longitude,
        Heading = 90d,
        RecordedAtUtc = recordedAtUtc
    };

    private static DriverWorkSession Session(double? latitude = 29.30d, double? longitude = 48.00d) => new()
    {
        DriverProfileId = Guid.NewGuid(),
        StartedAtUtc = NowUtc.AddHours(-1),
        StartLatitude = latitude,
        StartLongitude = longitude,
        Status = "Active"
    };

    // ---------- origin: stale tracking ----------

    [Fact]
    public async Task BuildAsync_WithStaleTracking_ReturnsUnavailable_DriverLocationStale_AndSkipsProvider()
    {
        var builder = CreateBuilder(out var routing);
        var tracking = Tracking(NowUtc.AddSeconds(-121)); // just past the 120s freshness window

        var navigation = await builder.BuildAsync(Guid.NewGuid(), Stop(), tracking, session: null, isArabic: true, CancellationToken.None);

        navigation.RouteStatus.Should().Be("Unavailable");
        navigation.UnavailableReason.Should().Be("driver_location_stale");
        navigation.EncodedPolyline.Should().BeNull();
        navigation.DistanceMeters.Should().BeNull();
        navigation.DurationSeconds.Should().BeNull();
        navigation.EstimatedArrivalAtUtc.Should().BeNull();
        navigation.CalculatedAtUtc.Should().BeNull();

        // The last known location is still drawable, marked stale.
        navigation.Origin.Should().NotBeNull();
        navigation.Origin!.IsStale.Should().BeTrue();
        navigation.Origin.Source.Should().Be("tracking");
        navigation.Origin.RecordedAtUtc.Should().Be(tracking.RecordedAtUtc);

        // The destination remains navigable through the external maps app.
        navigation.Destination.Should().NotBeNull();
        navigation.CanNavigate.Should().BeTrue();
        navigation.GoogleMapsUrl.Should().NotBeNull();

        await routing.DidNotReceive().GetDirectionsAsync(
            Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_WithFreshTracking_InsideWindow_IsNotStale()
    {
        var builder = CreateBuilder(out _);
        var tracking = Tracking(NowUtc.AddSeconds(-119));

        // Destination is missing so no provider call is required; the point of this
        // test is that a 119s-old fix passes the freshness gate (IsStale=false).
        var navigation = await builder.BuildAsync(Guid.NewGuid(), Stop(latitude: null, longitude: null), tracking, session: null, isArabic: true, CancellationToken.None);

        navigation.UnavailableReason.Should().Be("destination_location_missing");
        navigation.Origin.Should().NotBeNull();
        navigation.Origin!.IsStale.Should().BeFalse();
        navigation.Origin.Source.Should().Be("tracking");
    }

    // ---------- origin: missing / shift-start ----------

    [Fact]
    public async Task BuildAsync_WithoutTrackingOrSession_ReturnsUnavailable_DriverLocationMissing()
    {
        var builder = CreateBuilder(out var routing);

        var navigation = await builder.BuildAsync(Guid.NewGuid(), Stop(), tracking: null, session: null, isArabic: true, CancellationToken.None);

        navigation.RouteStatus.Should().Be("Unavailable");
        navigation.UnavailableReason.Should().Be("driver_location_missing");
        navigation.Origin.Should().BeNull();
        navigation.EncodedPolyline.Should().BeNull();

        navigation.Destination.Should().NotBeNull();
        navigation.CanNavigate.Should().BeTrue();
        navigation.GoogleMapsUrl.Should().NotBeNull();

        await routing.DidNotReceive().GetDirectionsAsync(
            Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_WithShiftStartOnly_ReturnsUnavailable_DriverLocationStale_WithShiftStartOrigin()
    {
        var builder = CreateBuilder(out var routing);
        var session = Session();

        // Even a brand-new session start is never a live location.
        var navigation = await builder.BuildAsync(Guid.NewGuid(), Stop(), tracking: null, session, isArabic: true, CancellationToken.None);

        navigation.RouteStatus.Should().Be("Unavailable");
        navigation.UnavailableReason.Should().Be("driver_location_stale");
        navigation.Origin.Should().NotBeNull();
        navigation.Origin!.Source.Should().Be("shift_start");
        navigation.Origin.IsStale.Should().BeTrue();
        navigation.Origin.RecordedAtUtc.Should().BeNull();
        navigation.Origin.Heading.Should().BeNull();
        navigation.Origin.Latitude.Should().Be(session.StartLatitude!.Value);
        navigation.EncodedPolyline.Should().BeNull();

        await routing.DidNotReceive().GetDirectionsAsync(
            Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_WithSessionWithoutCoordinates_ReturnsUnavailable_DriverLocationMissing()
    {
        var builder = CreateBuilder(out _);
        var session = Session(latitude: null, longitude: null);

        var navigation = await builder.BuildAsync(Guid.NewGuid(), Stop(), tracking: null, session, isArabic: true, CancellationToken.None);

        navigation.UnavailableReason.Should().Be("driver_location_missing");
        navigation.Origin.Should().BeNull();
    }

    // ---------- origin: invalid coordinates ----------

    [Fact]
    public async Task BuildAsync_WithZeroCoordinates_ReturnsUnavailable_DriverLocationInvalid()
    {
        var builder = CreateBuilder(out var routing);
        var tracking = Tracking(NowUtc.AddSeconds(-10), latitude: 0m, longitude: 0m);

        var navigation = await builder.BuildAsync(Guid.NewGuid(), Stop(), tracking, session: null, isArabic: true, CancellationToken.None);

        navigation.RouteStatus.Should().Be("Unavailable");
        navigation.UnavailableReason.Should().Be("driver_location_invalid");
        navigation.Origin.Should().BeNull("a (0,0) fix is not a drawable origin");
        navigation.EncodedPolyline.Should().BeNull();

        await routing.DidNotReceive().GetDirectionsAsync(
            Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_WithOutOfKuwaitTracking_ReturnsUnavailable_DriverLocationInvalid()
    {
        var builder = CreateBuilder(out _);
        var tracking = Tracking(NowUtc.AddSeconds(-10), latitude: 51.5074m, longitude: -0.1278m);

        var navigation = await builder.BuildAsync(Guid.NewGuid(), Stop(), tracking, session: null, isArabic: true, CancellationToken.None);

        navigation.UnavailableReason.Should().Be("driver_location_invalid");
        navigation.Origin.Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_WithTrackingFromFuture_BeyondTolerance_ReturnsUnavailable_DriverLocationInvalid()
    {
        var builder = CreateBuilder(out _);
        var tracking = Tracking(NowUtc.AddSeconds(31)); // more than 30s ahead of the server clock

        var navigation = await builder.BuildAsync(Guid.NewGuid(), Stop(), tracking, session: null, isArabic: true, CancellationToken.None);

        navigation.UnavailableReason.Should().Be("driver_location_invalid");
        navigation.Origin.Should().BeNull();
    }

    // ---------- destination ----------

    [Fact]
    public async Task BuildAsync_WithMissingDestination_ReturnsUnavailable_DestinationLocationMissing_KeepingOrigin()
    {
        var builder = CreateBuilder(out var routing);
        var tracking = Tracking(NowUtc.AddSeconds(-10));

        var navigation = await builder.BuildAsync(Guid.NewGuid(), Stop(latitude: null, longitude: null), tracking, session: null, isArabic: true, CancellationToken.None);

        navigation.RouteStatus.Should().Be("Unavailable");
        navigation.UnavailableReason.Should().Be("destination_location_missing");
        navigation.Destination.Should().BeNull();
        navigation.GoogleMapsUrl.Should().BeNull();
        navigation.CanNavigate.Should().BeFalse();
        navigation.EncodedPolyline.Should().BeNull();

        // The driver marker is still drawable.
        navigation.Origin.Should().NotBeNull();
        navigation.Origin!.IsStale.Should().BeFalse();

        await routing.DidNotReceive().GetDirectionsAsync(
            Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_WithDestinationOutsideKuwait_ReturnsUnavailable_DestinationLocationMissing()
    {
        var builder = CreateBuilder(out _);

        var navigation = await builder.BuildAsync(
            Guid.NewGuid(), Stop(latitude: 51.5074, longitude: -0.1278),
            tracking: Tracking(NowUtc.AddSeconds(-10)), session: null, isArabic: true, CancellationToken.None);

        navigation.UnavailableReason.Should().Be("destination_location_missing");
        navigation.Destination.Should().BeNull();
        navigation.CanNavigate.Should().BeFalse();
        navigation.GoogleMapsUrl.Should().BeNull();
    }

    // ---------- delivered stop ----------

    [Fact]
    public async Task BuildAsync_WithDeliveredStop_ReturnsCompleted_WithoutRouteOrLink()
    {
        var builder = CreateBuilder(out var routing);
        var tracking = Tracking(NowUtc.AddSeconds(-10));

        var navigation = await builder.BuildAsync(Guid.NewGuid(), Stop(status: "Delivered"), tracking, session: null, isArabic: true, CancellationToken.None);

        navigation.RouteStatus.Should().Be("Completed");
        navigation.UnavailableReason.Should().BeNull();
        navigation.CanNavigate.Should().BeFalse();
        navigation.EncodedPolyline.Should().BeNull();
        navigation.DistanceMeters.Should().BeNull();
        navigation.DurationSeconds.Should().BeNull();
        navigation.EstimatedArrivalAtUtc.Should().BeNull();
        navigation.CalculatedAtUtc.Should().BeNull();
        navigation.GoogleMapsUrl.Should().BeNull();

        // The delivered stop can still be shown as a card / marker.
        navigation.Destination.Should().NotBeNull();

        await routing.DidNotReceive().GetDirectionsAsync(
            Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    // ---------- labels & links ----------

    [Fact]
    public async Task BuildAsync_LocalizesOriginLabel_AndPrefersCustomerNameForDestination()
    {
        var builder = CreateBuilder(out _);
        var tracking = Tracking(NowUtc.AddSeconds(-10));

        var arabic = await builder.BuildAsync(Guid.NewGuid(), Stop(), tracking, session: null, isArabic: true, CancellationToken.None);
        arabic.Origin!.Label.Should().Be("موقعك الحالي");
        arabic.Destination!.Label.Should().Be("محمد علي");

        var english = await builder.BuildAsync(Guid.NewGuid(), Stop(customerName: "Mohammed Ali"), tracking, session: null, isArabic: false, CancellationToken.None);
        english.Origin!.Label.Should().Be("Your location");
        english.Destination!.Label.Should().Be("Mohammed Ali");
    }

    [Fact]
    public async Task BuildAsync_WithoutCustomerName_UsesLocalizedDeliveryDestinationLabel()
    {
        var builder = CreateBuilder(out _);
        var tracking = Tracking(NowUtc.AddSeconds(-10));

        var arabic = await builder.BuildAsync(Guid.NewGuid(), Stop(customerName: string.Empty), tracking, session: null, isArabic: true, CancellationToken.None);
        arabic.Destination!.Label.Should().Be("وجهة التوصيل");

        var english = await builder.BuildAsync(Guid.NewGuid(), Stop(customerName: null), tracking, session: null, isArabic: false, CancellationToken.None);
        english.Destination!.Label.Should().Be("Delivery destination");
    }

    [Fact]
    public async Task BuildAsync_GoogleMapsUrl_IsCultureInvariant_AndTargetsDestinationOnly()
    {
        var builder = CreateBuilder(out _);
        var tracking = Tracking(NowUtc.AddSeconds(-10));

        var navigation = await builder.BuildAsync(Guid.NewGuid(), Stop(latitude: 29.3380, longitude: 48.0230), tracking, session: null, isArabic: true, CancellationToken.None);

        navigation.GoogleMapsUrl.Should().Be(
            "https://www.google.com/maps/dir/?api=1&destination=29.338%2C48.023&travelmode=driving&dir_action=navigate");
        navigation.GoogleMapsUrl.Should().NotContain("،").And.NotContain("origin=");
    }

    // ---------- real routing provider ----------

    [Fact]
    public async Task BuildAsync_WithRealProviderRoute_ReturnsReady_WithPolylineMetricsAndEta()
    {
        var builder = CreateBuilder(out var routing);
        ConfigureProviderRoute(routing);
        var tracking = Tracking(NowUtc.AddSeconds(-5));

        var navigation = await builder.BuildAsync(Guid.NewGuid(), Stop(), tracking, session: null, isArabic: true, CancellationToken.None);

        navigation.RouteStatus.Should().Be("Ready");
        navigation.UnavailableReason.Should().BeNull();
        navigation.EncodedPolyline.Should().Be("_p~iF~ps|U_ulLnnqC");
        navigation.PolylineEncoding.Should().Be("google_polyline5");
        navigation.DistanceMeters.Should().Be(5200);
        navigation.DurationSeconds.Should().Be(480);
        navigation.CalculatedAtUtc.Should().Be(NowUtc);
        navigation.EstimatedArrivalAtUtc.Should().Be(NowUtc.AddSeconds(480));
        navigation.CanNavigate.Should().BeTrue();
        navigation.GoogleMapsUrl.Should().NotBeNull();

        await routing.Received(1).GetDirectionsAsync(
            (decimal)tracking.Latitude, (decimal)tracking.Longitude,
            29.3380m, 48.0230m, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_WithZeroLengthProviderRoute_ReturnsReady_WithZeroMetrics()
    {
        var builder = CreateBuilder(out var routing);
        routing.GetDirectionsAsync(
                Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(_ => ProviderRoute(polyline: "??", distanceMeters: 0, durationSeconds: 0));
        var tracking = Tracking(NowUtc.AddSeconds(-5));

        var navigation = await builder.BuildAsync(Guid.NewGuid(), Stop(), tracking, session: null, isArabic: true, CancellationToken.None);

        // A zero-length trip is legitimate; no artificial minute is forced.
        navigation.RouteStatus.Should().Be("Ready");
        navigation.DistanceMeters.Should().Be(0);
        navigation.DurationSeconds.Should().Be(0);
        navigation.EstimatedArrivalAtUtc.Should().Be(navigation.CalculatedAtUtc);
    }

    [Fact]
    public async Task BuildAsync_WithFallbackEstimate_ReturnsUnavailable_DirectionsUnavailable_IgnoringProviderNumbers()
    {
        var builder = CreateBuilder(out var routing);
        routing.GetDirectionsAsync(
                Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(_ => ProviderRoute(polyline: "fake-geometry", distanceMeters: 5200, durationSeconds: 480, isFallbackEstimate: true));
        var tracking = Tracking(NowUtc.AddSeconds(-5));

        var navigation = await builder.BuildAsync(Guid.NewGuid(), Stop(), tracking, session: null, isArabic: true, CancellationToken.None);

        navigation.RouteStatus.Should().Be("Unavailable");
        navigation.UnavailableReason.Should().Be("directions_unavailable");
        navigation.EncodedPolyline.Should().BeNull();
        navigation.DistanceMeters.Should().BeNull();
        navigation.DurationSeconds.Should().BeNull();
        navigation.EstimatedArrivalAtUtc.Should().BeNull();
        navigation.CalculatedAtUtc.Should().BeNull();

        // Pins stay drawable and the external navigation link stays available.
        navigation.Origin.Should().NotBeNull();
        navigation.Destination.Should().NotBeNull();
        navigation.CanNavigate.Should().BeTrue();
        navigation.GoogleMapsUrl.Should().NotBeNull();
    }

    [Fact]
    public async Task BuildAsync_WithEmptyProviderGeometry_ReturnsUnavailable_DirectionsUnavailable()
    {
        var builder = CreateBuilder(out var routing);
        routing.GetDirectionsAsync(
                Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(_ => ProviderRoute(polyline: string.Empty));
        var tracking = Tracking(NowUtc.AddSeconds(-5));

        var navigation = await builder.BuildAsync(Guid.NewGuid(), Stop(), tracking, session: null, isArabic: true, CancellationToken.None);

        navigation.RouteStatus.Should().Be("Unavailable");
        navigation.UnavailableReason.Should().Be("directions_unavailable");
        navigation.EncodedPolyline.Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_WithNegativeProviderMetrics_ReturnsUnavailable_DirectionsUnavailable()
    {
        var builder = CreateBuilder(out var routing);
        routing.GetDirectionsAsync(
                Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(_ => ProviderRoute(distanceMeters: -5));
        var tracking = Tracking(NowUtc.AddSeconds(-5));

        var navigation = await builder.BuildAsync(Guid.NewGuid(), Stop(), tracking, session: null, isArabic: true, CancellationToken.None);

        navigation.UnavailableReason.Should().Be("directions_unavailable");
        navigation.EncodedPolyline.Should().BeNull();
        navigation.DistanceMeters.Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_WithProviderFailure_ReturnsUnavailable_DirectionsUnavailable()
    {
        var builder = CreateBuilder(out var routing);
        routing.GetDirectionsAsync(
                Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<RouteDirectionsResult>(Error.Failure("directions.provider_failure")));
        var tracking = Tracking(NowUtc.AddSeconds(-5));

        var navigation = await builder.BuildAsync(Guid.NewGuid(), Stop(), tracking, session: null, isArabic: true, CancellationToken.None);

        navigation.RouteStatus.Should().Be("Unavailable");
        navigation.UnavailableReason.Should().Be("directions_unavailable");
    }

    [Fact]
    public async Task BuildAsync_WhenProviderThrows_ReturnsUnavailable_DirectionsUnavailable()
    {
        var builder = CreateBuilder(out var routing);
        routing.GetDirectionsAsync(
                Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Result<RouteDirectionsResult>>(new HttpRequestException("provider unreachable")));
        var tracking = Tracking(NowUtc.AddSeconds(-5));

        var navigation = await builder.BuildAsync(Guid.NewGuid(), Stop(), tracking, session: null, isArabic: true, CancellationToken.None);

        navigation.RouteStatus.Should().Be("Unavailable");
        navigation.UnavailableReason.Should().Be("directions_unavailable");
        navigation.EncodedPolyline.Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_WhenProviderCancels_PropagatesCancellation()
    {
        var builder = CreateBuilder(out var routing);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        routing.GetDirectionsAsync(
                Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Result<RouteDirectionsResult>>(new OperationCanceledException(cts.Token)));

        var act = () => builder.BuildAsync(Guid.NewGuid(), Stop(), Tracking(NowUtc.AddSeconds(-5)), session: null, isArabic: true, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
    }

    // ---------- route cache ----------

    [Fact]
    public async Task BuildAsync_SecondIdenticalRequest_IsServedFromCache_WithProviderCalledOnce()
    {
        var builder = CreateBuilder(out var routing);
        ConfigureProviderRoute(routing);
        var driverId = Guid.NewGuid();
        var tracking = Tracking(NowUtc.AddSeconds(-5));
        var stop = Stop();

        var first = await builder.BuildAsync(driverId, stop, tracking, session: null, isArabic: true, CancellationToken.None);
        var second = await builder.BuildAsync(driverId, stop, tracking, session: null, isArabic: true, CancellationToken.None);

        first.RouteStatus.Should().Be("Ready");
        second.RouteStatus.Should().Be("Ready");
        second.EncodedPolyline.Should().Be(first.EncodedPolyline);
        second.CalculatedAtUtc.Should().Be(first.CalculatedAtUtc);
        second.DistanceMeters.Should().Be(first.DistanceMeters);
        second.DurationSeconds.Should().Be(first.DurationSeconds);

        await routing.Received(1).GetDirectionsAsync(
            Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_DifferentStop_OrOrigin_BypassesCache_AndCallsProviderAgain()
    {
        var builder = CreateBuilder(out var routing);
        ConfigureProviderRoute(routing);
        var driverId = Guid.NewGuid();
        var tracking = Tracking(NowUtc.AddSeconds(-5));

        await builder.BuildAsync(driverId, Stop(), tracking, session: null, isArabic: true, CancellationToken.None);
        await builder.BuildAsync(driverId, Stop(stopId: Guid.NewGuid()), tracking, session: null, isArabic: true, CancellationToken.None);
        await builder.BuildAsync(driverId, Stop(), Tracking(NowUtc.AddSeconds(-5), latitude: 29.3376m), session: null, isArabic: true, CancellationToken.None);
        await builder.BuildAsync(driverId, Stop(latitude: 29.3390, longitude: 48.0240), tracking, session: null, isArabic: true, CancellationToken.None);

        await routing.Received(4).GetDirectionsAsync(
            Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_DoesNotCacheProviderFailure_OrFallbackEstimate()
    {
        var builder = CreateBuilder(out var routing);
        var driverId = Guid.NewGuid();
        var tracking = Tracking(NowUtc.AddSeconds(-5));
        var stop = Stop();

        routing.GetDirectionsAsync(
                Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<RouteDirectionsResult>(Error.Failure("directions.provider_failure")));
        var firstFailure = await builder.BuildAsync(driverId, stop, tracking, session: null, isArabic: true, CancellationToken.None);
        var secondFailure = await builder.BuildAsync(driverId, stop, tracking, session: null, isArabic: true, CancellationToken.None);

        firstFailure.UnavailableReason.Should().Be("directions_unavailable");
        secondFailure.UnavailableReason.Should().Be("directions_unavailable");

        routing.GetDirectionsAsync(
                Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(_ => ProviderRoute(isFallbackEstimate: true));
        var firstFallback = await builder.BuildAsync(driverId, stop, tracking, session: null, isArabic: true, CancellationToken.None);
        var secondFallback = await builder.BuildAsync(driverId, stop, tracking, session: null, isArabic: true, CancellationToken.None);

        firstFallback.UnavailableReason.Should().Be("directions_unavailable");
        secondFallback.UnavailableReason.Should().Be("directions_unavailable");

        // Both the failure and the fallback estimate were re-requested: neither
        // ever entered the success cache.
        await routing.Received(4).GetDirectionsAsync(
            Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_StaleLocation_IsRecheckedBeforeCacheLookup()
    {
        var builder = CreateBuilder(out var routing);
        ConfigureProviderRoute(routing);
        var driverId = Guid.NewGuid();
        var stop = Stop();

        await builder.BuildAsync(driverId, stop, Tracking(NowUtc.AddSeconds(-5)), session: null, isArabic: true, CancellationToken.None);

        // The same stop, but the driver's fix went silent — the cached route must
        // not be served for a stale location.
        var stale = await builder.BuildAsync(driverId, stop, Tracking(NowUtc.AddSeconds(-121)), session: null, isArabic: true, CancellationToken.None);

        stale.RouteStatus.Should().Be("Unavailable");
        stale.UnavailableReason.Should().Be("driver_location_stale");
        stale.EncodedPolyline.Should().BeNull();

        await routing.Received(1).GetDirectionsAsync(
            Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }
}
