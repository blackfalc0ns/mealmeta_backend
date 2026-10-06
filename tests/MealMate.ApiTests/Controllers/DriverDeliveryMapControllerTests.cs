using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using MealMate.Api.Controllers.V1;
using MealMate.Api.Realtime;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.Controllers;

/// <summary>
/// Contract tests for GET /api/v1/driver/map/route: authentication, query
/// binding, the DriverMap.StopNotFound problem contract, and the JSON shape of
/// the new navigation payload (camelCase keys, exact status/reason strings,
/// meters/seconds units, google_polyline5 encoding, null-preserved keys).
/// </summary>
public class DriverDeliveryMapControllerTests
{
    private static readonly Guid DriverId = Guid.NewGuid();
    private static readonly Guid StopId = Guid.NewGuid();

    private static DriverMapStopDto StopDto(string status = "InTransit", string customerName = "محمد علي") => new(
        StopId: StopId,
        BoxId: StopId,
        TripId: Guid.NewGuid(),
        SequenceBadge: "1/2",
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
        StatusColor: "green",
        IsCurrent: true,
        Latitude: 29.3380,
        Longitude: 48.0230);

    private static DriverDeliveryMapResponse ReadyResponse() => new(
        TripId: Guid.NewGuid(),
        TripCode: "TRP-1256",
        DriverLatitude: 29.3375,
        DriverLongitude: 48.0280,
        TotalStopsCount: 2,
        CompletedStopsCount: 0,
        FocusedStop: StopDto(),
        Stops: new List<DriverMapStopDto> { StopDto(), StopDto(status: "Pending", customerName: "مهند أحمد") },
        RoutePolylineWaypoints: new List<DriverMapWaypointDto> { new(29.3375, 48.0280), new(29.3380, 48.0230) },
        Navigation: new DriverNavigationDto(
            DestinationStopId: StopId,
            Origin: new DriverNavigationPointDto(
                Latitude: 29.3375, Longitude: 48.0280, Label: "موقعك الحالي", Source: "tracking",
                RecordedAtUtc: new DateTime(2026, 10, 5, 7, 59, 50, DateTimeKind.Utc), IsStale: false, Heading: 90d),
            Destination: new DriverNavigationPointDto(
                Latitude: 29.3380, Longitude: 48.0230, Label: "محمد علي", Source: "trip_stop",
                RecordedAtUtc: null, IsStale: false, Heading: null),
            RouteStatus: "Ready",
            UnavailableReason: null,
            EncodedPolyline: "_p~iF~ps|U_ulLnnqC",
            PolylineEncoding: "google_polyline5",
            DistanceMeters: 5200,
            DurationSeconds: 480,
            EstimatedArrivalAtUtc: new DateTime(2026, 10, 5, 8, 8, 0, DateTimeKind.Utc),
            CalculatedAtUtc: new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc),
            GoogleMapsUrl: "https://www.google.com/maps/dir/?api=1&destination=29.338%2C48.023&travelmode=driving&dir_action=navigate",
            CanNavigate: true));

    private static (DriverHomeController Controller, ISender Sender) CreateController(
        Result<DriverDeliveryMapResponse>? mapResult = null)
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<IRequest<Result<DriverDeliveryMapResponse>>>(), Arg.Any<CancellationToken>())
            .Returns(mapResult ?? Result.Success(ReadyResponse()));

        var services = new ServiceCollection();
        services.AddSingleton(sender);
        services.AddSingleton(Substitute.For<IHubContext<DispatcherHub>>());
        services.AddSingleton(Substitute.For<IHubContext<DriverHub>>());

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var controller = new DriverHomeController(
            Substitute.For<IHubContext<DispatcherHub>>(),
            Substitute.For<IHubContext<DriverHub>>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return (controller, sender);
    }

    [Fact]
    public async Task GetDeliveryMap_BindsDriverId_AndFocusedStopId_IntoTheQuery()
    {
        var (controller, sender) = CreateController();
        var focusedStopId = Guid.NewGuid();

        await controller.GetDeliveryMap(DriverId, focusedStopId, CancellationToken.None);

        await sender.Received(1).Send(
            Arg.Is<GetDriverDeliveryMapQuery>(q => q.DriverId == DriverId && q.FocusedStopId == focusedStopId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDeliveryMap_StopNotFound_Returns404Problem_WithStableCode()
    {
        var (controller, _) = CreateController(
            Result.Failure<DriverDeliveryMapResponse>(Error.NotFound(
                "DriverMap.StopNotFound",
                "The requested stop is not part of your active deliveries.")));

        var result = await controller.GetDeliveryMap(null, Guid.NewGuid(), CancellationToken.None);

        var problem = result as ObjectResult;
        problem.Should().NotBeNull();
        problem!.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
        var details = problem.Value as ProblemDetails;
        details.Should().NotBeNull();
        details!.Extensions["code"].Should().Be("DriverMap.StopNotFound");
    }

    [Fact]
    public async Task GetDeliveryMap_TripNotFound_Returns404Problem_WithStableCode()
    {
        var (controller, _) = CreateController(
            Result.Failure<DriverDeliveryMapResponse>(Error.NotFound(
                "DriverTrip.NotFound",
                "No active delivery trip found for this driver.")));

        var result = await controller.GetDeliveryMap(null, null, CancellationToken.None);

        var problem = result as ObjectResult;
        problem.Should().NotBeNull();
        problem!.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
        ((ProblemDetails)problem.Value!).Extensions["code"].Should().Be("DriverTrip.NotFound");
    }

    [Fact]
    public async Task GetDeliveryMap_SerializesNavigation_CamelCase_ExactStatusStrings_AndUnits()
    {
        var (controller, _) = CreateController();

        var result = await controller.GetDeliveryMap(null, null, CancellationToken.None);

        var ok = result as OkObjectResult;
        ok.Should().NotBeNull();
        ok!.StatusCode.Should().Be((int)HttpStatusCode.OK);

        var json = JsonSerializer.Serialize(ok.Value!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Top-level contract keys survive (camelCase), driver coordinates included.
        root.GetProperty("tripId").ValueKind.Should().Be(JsonValueKind.String);
        root.GetProperty("tripCode").GetString().Should().Be("TRP-1256");
        root.GetProperty("driverLatitude").GetDouble().Should().Be(29.3375);
        root.GetProperty("totalStopsCount").GetInt32().Should().Be(2);
        root.GetProperty("stops").GetArrayLength().Should().Be(2);
        root.GetProperty("routePolylineWaypoints").GetArrayLength().Should().Be(2);

        // Card phones are never serialized for the driver map.
        root.GetProperty("focusedStop").GetProperty("customerPhone").GetString().Should().BeEmpty();

        var navigation = root.GetProperty("navigation");
        navigation.GetProperty("destinationStopId").GetGuid().Should().Be(StopId);
        navigation.GetProperty("routeStatus").GetString().Should().Be("Ready");
        navigation.GetProperty("unavailableReason").ValueKind.Should().Be(JsonValueKind.Null);
        navigation.GetProperty("polylineEncoding").GetString().Should().Be("google_polyline5");
        navigation.GetProperty("encodedPolyline").GetString().Should().Be("_p~iF~ps|U_ulLnnqC");
        navigation.GetProperty("distanceMeters").GetInt64().Should().Be(5200);
        navigation.GetProperty("durationSeconds").GetInt64().Should().Be(480);
        navigation.GetProperty("calculatedAtUtc").GetDateTime().Should().Be(new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc));
        navigation.GetProperty("estimatedArrivalAtUtc").GetDateTime().Should().Be(new DateTime(2026, 10, 5, 8, 8, 0, DateTimeKind.Utc));
        navigation.GetProperty("googleMapsUrl").GetString().Should().StartWith("https://www.google.com/maps/dir/?api=1&destination=");
        navigation.GetProperty("canNavigate").GetBoolean().Should().BeTrue();

        var origin = navigation.GetProperty("origin");
        origin.GetProperty("latitude").GetDouble().Should().Be(29.3375);
        origin.GetProperty("source").GetString().Should().Be("tracking");
        origin.GetProperty("recordedAtUtc").ValueKind.Should().Be(JsonValueKind.String);
        origin.GetProperty("isStale").GetBoolean().Should().BeFalse();
        origin.GetProperty("heading").GetDouble().Should().Be(90d);

        var destination = navigation.GetProperty("destination");
        destination.GetProperty("source").GetString().Should().Be("trip_stop");
        destination.GetProperty("recordedAtUtc").ValueKind.Should().Be(JsonValueKind.Null);
        destination.GetProperty("heading").ValueKind.Should().Be(JsonValueKind.Null);
        destination.GetProperty("label").GetString().Should().Be("محمد علي");
    }

    [Fact]
    public async Task GetDeliveryMap_WithoutNavigation_StillExposesTheKeyAsNull()
    {
        // Responses built before the navigation field existed (or with it unset)
        // must keep the key in the JSON so older mobile builds see a stable shape.
        var responseWithoutNavigation = ReadyResponse() with { Navigation = null };
        var (controller, _) = CreateController(Result.Success(responseWithoutNavigation));

        var result = await controller.GetDeliveryMap(null, null, CancellationToken.None);

        var ok = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(ok.Value!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("navigation").ValueKind.Should().Be(JsonValueKind.Null);
    }
}

/// <summary>
/// Full-pipeline check against the real endpoint routing table: the route must be
/// registered (401 from authorization, not the routing 404 that breaks mobile).
/// </summary>
public sealed class DriverDeliveryMapEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public DriverDeliveryMapEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Seed:Identity", "false");
        }).CreateClient();
    }

    [Fact]
    public async Task Unauthenticated_GetDeliveryMap_ReturnsUnauthorized_NotRouting404()
    {
        var response = await _client.GetAsync("/api/v1/driver/map/route");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unauthenticated_GetDeliveryMap_WithFocusedStopId_ReturnsUnauthorized_NotRouting404()
    {
        var response = await _client.GetAsync($"/api/v1/driver/map/route?focusedStopId={Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
