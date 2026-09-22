using System.Reflection;
using FluentAssertions;
using MealMate.Api.Controllers.V1;
using MealMate.Api.Realtime;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.Controllers;

public class DispatcherDashboardControllerTests
{
    [Fact]
    public void Controller_HasCorrectRouteAttribute()
    {
        var routeAttr = typeof(DispatcherDashboardController).GetCustomAttribute<RouteAttribute>();
        routeAttr.Should().NotBeNull();
        routeAttr!.Template.Should().Be("api/v1/dispatcher");
    }

    [Fact]
    public void ActionMethods_HaveCorrectHttpAttributesAndRoutes()
    {
        var overviewMethod = typeof(DispatcherDashboardController).GetMethod(nameof(DispatcherDashboardController.GetDashboardOverview));
        overviewMethod.Should().NotBeNull();
        var overviewGet = overviewMethod!.GetCustomAttribute<HttpGetAttribute>();
        overviewGet.Should().NotBeNull();
        overviewGet!.Template.Should().Be("dashboard/overview");

        var rosterMethod = typeof(DispatcherDashboardController).GetMethod(nameof(DispatcherDashboardController.GetDriverRoster));
        rosterMethod.Should().NotBeNull();
        var rosterGet = rosterMethod!.GetCustomAttribute<HttpGetAttribute>();
        rosterGet.Should().NotBeNull();
        rosterGet!.Template.Should().Be("drivers/roster");

        var locationsMethod = typeof(DispatcherDashboardController).GetMethod(nameof(DispatcherDashboardController.GetLiveDriverLocations));
        locationsMethod.Should().NotBeNull();
        var locationsGet = locationsMethod!.GetCustomAttribute<HttpGetAttribute>();
        locationsGet.Should().NotBeNull();
        locationsGet!.Template.Should().Be("drivers/live-locations");

        var updateMethod = typeof(DispatcherDashboardController).GetMethod(nameof(DispatcherDashboardController.UpdateDriverLocation));
        updateMethod.Should().NotBeNull();
        var postAttrs = updateMethod!.GetCustomAttributes<HttpPostAttribute>().ToList();
        postAttrs.Should().HaveCount(2);
        postAttrs.Select(a => a.Template).Should().Contain("drivers/location");
        postAttrs.Select(a => a.Template).Should().Contain("/api/v1/driver/location");

        var routeCalcMethod = typeof(DispatcherDashboardController).GetMethod(nameof(DispatcherDashboardController.CalculateRoute));
        routeCalcMethod.Should().NotBeNull();
        var routePost = routeCalcMethod!.GetCustomAttribute<HttpPostAttribute>();
        routePost.Should().NotBeNull();
        routePost!.Template.Should().Be("routes/calculate");

        var directionsMethod = typeof(DispatcherDashboardController).GetMethod(nameof(DispatcherDashboardController.GetDirections));
        directionsMethod.Should().NotBeNull();
        var dirPost = directionsMethod!.GetCustomAttribute<HttpPostAttribute>();
        dirPost.Should().NotBeNull();
        dirPost!.Template.Should().Be("routes/directions");

        var queueMethod = typeof(DispatcherDashboardController).GetMethod(nameof(DispatcherDashboardController.GetOrderQueue));
        queueMethod.Should().NotBeNull();
        var queueGet = queueMethod!.GetCustomAttribute<HttpGetAttribute>();
        queueGet.Should().NotBeNull();
        queueGet!.Template.Should().Be("orders/queue");

        var candidatesMethod = typeof(DispatcherDashboardController).GetMethod(nameof(DispatcherDashboardController.GetBoxCandidates));
        candidatesMethod.Should().NotBeNull();
        var candidatesGet = candidatesMethod!.GetCustomAttribute<HttpGetAttribute>();
        candidatesGet.Should().NotBeNull();
        candidatesGet!.Template.Should().Be("orders/{boxId:guid}/candidates");

        var detailsMethod = typeof(DispatcherDashboardController).GetMethod(nameof(DispatcherDashboardController.GetAssignmentDetails));
        detailsMethod.Should().NotBeNull();
        var detailsGet = detailsMethod!.GetCustomAttribute<HttpGetAttribute>();
        detailsGet.Should().NotBeNull();
        detailsGet!.Template.Should().Be("orders/{boxId:guid}/assignment-details");

        var summaryMethod = typeof(DispatcherDashboardController).GetMethod(nameof(DispatcherDashboardController.GetBoxSummary));
        summaryMethod.Should().NotBeNull();
        var summaryGet = summaryMethod!.GetCustomAttribute<HttpGetAttribute>();
        summaryGet.Should().NotBeNull();
        summaryGet!.Template.Should().Be("orders/{boxId:guid}/summary");

        var assignMethod = typeof(DispatcherDashboardController).GetMethod(nameof(DispatcherDashboardController.AssignBoxToDriver));
        assignMethod.Should().NotBeNull();
        var assignPost = assignMethod!.GetCustomAttribute<HttpPostAttribute>();
        assignPost.Should().NotBeNull();
        assignPost!.Template.Should().Be("orders/{boxId:guid}/assign");
    }

    [Fact]
    public async Task CalculateRoute_Returns_Ok_With_DistanceDurationData()
    {
        // Arrange
        var hub = Substitute.For<IHubContext<DispatcherHub>>();
        var routing = Substitute.For<IGoogleRoutingService>();

        var expected = new RouteDistanceDurationResult(
            DistanceMeters: 12500,
            DistanceKm: 12.5,
            DurationSeconds: 1200,
            DurationMinutes: 20,
            DistanceText: "12.5 كم",
            DurationText: "20 دقيقة",
            IsFallbackEstimate: true);

        routing.CalculateDistanceAndDurationAsync(Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(expected));

        var controller = new DispatcherDashboardController(hub, routing)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var request = new CalculateRouteRequest(29.3333m, 48.0667m, 29.3759m, 47.9774m);

        // Act
        var result = await controller.CalculateRoute(request);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        okResult.Value.Should().Be(expected);
    }
}
