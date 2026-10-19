using System.Reflection;
using FluentAssertions;
using MealMate.Api.Controllers.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MealMate.ApiTests.Controllers;

public class RestaurantDeliveryTripsControllerTests
{
    [Fact]
    public void Controller_RequiresAuthorization_And_HasNoAllowAnonymous()
    {
        var authAttr = typeof(RestaurantDeliveryTripsController).GetCustomAttribute<AuthorizeAttribute>();
        authAttr.Should().NotBeNull("RestaurantDeliveryTripsController must require authorization.");

        var allowAnon = typeof(RestaurantDeliveryTripsController).GetCustomAttribute<AllowAnonymousAttribute>();
        allowAnon.Should().BeNull("RestaurantDeliveryTripsController must not allow anonymous access.");
    }

    [Fact]
    public void Controller_HasRestaurantStaffPolicy()
    {
        var authAttr = typeof(RestaurantDeliveryTripsController).GetCustomAttribute<AuthorizeAttribute>();
        authAttr.Should().NotBeNull();
        authAttr!.Policy.Should().Be("RestaurantStaffPolicy", "Restaurant trips must be protected by RestaurantStaffPolicy.");
    }

    [Fact]
    public void Endpoints_HaveExpectedRouteAttributes()
    {
        var listMethod = typeof(RestaurantDeliveryTripsController).GetMethod(nameof(RestaurantDeliveryTripsController.List));
        listMethod.Should().NotBeNull();
        listMethod!.GetCustomAttribute<HttpGetAttribute>().Should().NotBeNull();

        var getMethod = typeof(RestaurantDeliveryTripsController).GetMethod(nameof(RestaurantDeliveryTripsController.Get));
        getMethod.Should().NotBeNull();
        getMethod!.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("{id:guid}");

        var statusMethod = typeof(RestaurantDeliveryTripsController).GetMethod(nameof(RestaurantDeliveryTripsController.UpdateStatus));
        statusMethod.Should().NotBeNull();
        statusMethod!.GetCustomAttribute<HttpPutAttribute>()!.Template.Should().Be("{id:guid}/status");

        var stopStatusMethod = typeof(RestaurantDeliveryTripsController).GetMethod(nameof(RestaurantDeliveryTripsController.UpdateStopStatus));
        stopStatusMethod.Should().NotBeNull();
        stopStatusMethod!.GetCustomAttribute<HttpPutAttribute>()!.Template.Should().Be("{id:guid}/stops/{stopId:guid}/status");
    }
}
