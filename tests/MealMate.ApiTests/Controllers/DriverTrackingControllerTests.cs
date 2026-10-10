using System.Reflection;
using FluentAssertions;
using MealMate.Api.Controllers.V1;
using MealMate.Api.Realtime;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.Controllers;

public class DriverTrackingControllerTests
{
    [Fact]
    public void Controller_HasCorrectRouteAttribute()
    {
        var routeAttr = typeof(DriverTrackingController).GetCustomAttribute<RouteAttribute>();
        routeAttr.Should().NotBeNull();
        routeAttr!.Template.Should().Be("api/v1/driver");
    }

    [Fact]
    public void Controller_RequiresAuthorization_WithoutDispatcherPolicy()
    {
        // Plain [Authorize]: any authenticated user passes the gate; the command handler
        // then resolves the driver from the JWT, so non-drivers get DriverNotFound.
        // It must NOT be DispatcherPolicy — a driver token can never satisfy it (403).
        var authAttr = typeof(DriverTrackingController).GetCustomAttribute<AuthorizeAttribute>();
        authAttr.Should().NotBeNull("DriverTrackingController must require authorization.");
        authAttr!.Policy.Should().BeNull("plain [Authorize] — DispatcherPolicy would reject driver tokens.");

        var allowAnon = typeof(DriverTrackingController).GetCustomAttribute<AllowAnonymousAttribute>();
        allowAnon.Should().BeNull("DriverTrackingController must not allow anonymous access.");
    }

    [Fact]
    public void UpdateDriverLocation_HasCorrectRoute()
    {
        var method = typeof(DriverTrackingController).GetMethod(nameof(DriverTrackingController.UpdateDriverLocation));
        method.Should().NotBeNull();

        var post = method!.GetCustomAttribute<HttpPostAttribute>();
        post.Should().NotBeNull();
        post!.Template.Should().Be("location", "driver app posts to POST /api/v1/driver/location");
    }

    [Fact]
    public async Task UpdateDriverLocation_SendsCommand_WithoutBodyIdentity_And_Returns_Ok()
    {
        // Identity comes from the JWT; the requested trip is validated by the handler.
        var sender = Substitute.For<ISender>();
        var response = new UpdateDriverLocationResponse(
            TrackingPointId: Guid.NewGuid(),
            RecordedAtUtc: DateTime.UtcNow,
            Message: "Driver location updated successfully",
            DriverId: Guid.NewGuid(),
            TripId: Guid.NewGuid(),
            RestaurantId: Guid.NewGuid(),
            Latitude: 31.123078m,
            Longitude: 29.7875195m,
            Heading: 11.9,
            SpeedKmh: 2.3,
            LocationZone: "Yarmouk",
            RemainingDistanceKm: 4.5,
            RemainingDistanceText: "4.5 km");
        sender.Send(Arg.Any<UpdateDriverLocationCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<UpdateDriverLocationResponse>.Success(response));

        var controller = new DriverTrackingController(new DriverLocationBroadcaster(Substitute.For<IHubContext<DispatcherHub>>()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        // ApiControllerBase.Sender resolves from RequestServices
        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(ISender)).Returns(sender);
        controller.ControllerContext.HttpContext.RequestServices = services;

        var request = new UpdateDriverLocationRequest(
            DriverId: Guid.NewGuid(),
            TripId: response.TripId,
            Latitude: 31.123078m,
            Longitude: 29.7875195m,
            Heading: 11.9,
            SpeedKmh: 2.3);

        var actionResult = await controller.UpdateDriverLocation(request, CancellationToken.None);

        actionResult.Should().BeOfType<OkObjectResult>();
        await sender.Received(1).Send(
            Arg.Is<UpdateDriverLocationCommand>(c =>
                c.DriverId == null &&
                c.TripId == response.TripId &&
                c.Latitude == 31.123078m &&
                c.Longitude == 29.7875195m &&
                c.Heading == 11.9 &&
                c.SpeedKmh == 2.3),
            Arg.Any<CancellationToken>());
    }
}
