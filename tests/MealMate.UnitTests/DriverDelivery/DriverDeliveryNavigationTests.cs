using FluentAssertions;
using MealMate.Application.Common.Abstractions;
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
            new FixedClock(now ?? NowUtc),
            Options.Create(new DriverNavigationOptions()));
    }

    private static DriverMapStopDto Stop(
        string status = "Pending",
        double? latitude = 29.3380,
        double? longitude = 48.0230,
        string customerName = "محمد علي",
        Guid? stopId = null) => new(
        StopId: stopId ?? Guid.NewGuid(),
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
}
