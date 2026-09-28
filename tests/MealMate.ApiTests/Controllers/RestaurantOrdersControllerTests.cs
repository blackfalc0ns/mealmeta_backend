using System.Reflection;
using FluentAssertions;
using MealMate.Api.Controllers.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MealMate.ApiTests.Controllers;

public class RestaurantOrdersControllerTests
{
    [Fact]
    public void Controller_RequiresAuthorization_And_HasNoAllowAnonymous()
    {
        var authAttr = typeof(RestaurantOrdersController).GetCustomAttribute<AuthorizeAttribute>();
        authAttr.Should().NotBeNull("RestaurantOrdersController must require authorization.");

        var allowAnon = typeof(RestaurantOrdersController).GetCustomAttribute<AllowAnonymousAttribute>();
        allowAnon.Should().BeNull("RestaurantOrdersController must not allow anonymous access.");
    }

    [Fact]
    public void Controller_HasRestaurantStaffPolicy()
    {
        var authAttr = typeof(RestaurantOrdersController).GetCustomAttribute<AuthorizeAttribute>();
        authAttr.Should().NotBeNull();
        authAttr!.Policy.Should().Be("RestaurantStaffPolicy", "Restaurant orders must be protected by RestaurantStaffPolicy.");
    }

    [Fact]
    public void Endpoints_HaveExpectedRouteAttributes()
    {
        var pendingMethod = typeof(RestaurantOrdersController).GetMethod(nameof(RestaurantOrdersController.GetPendingConfirmation));
        pendingMethod.Should().NotBeNull();
        var pendingGet = pendingMethod!.GetCustomAttribute<HttpGetAttribute>();
        pendingGet.Should().NotBeNull();
        pendingGet!.Template.Should().Be("pending-confirmation");

        var confirmMethod = typeof(RestaurantOrdersController).GetMethod(nameof(RestaurantOrdersController.ConfirmOrder));
        confirmMethod.Should().NotBeNull();
        var confirmPost = confirmMethod!.GetCustomAttribute<HttpPostAttribute>();
        confirmPost.Should().NotBeNull();
        confirmPost!.Template.Should().Be("{id:guid}/confirm");

        var rejectMethod = typeof(RestaurantOrdersController).GetMethod(nameof(RestaurantOrdersController.RejectOrder));
        rejectMethod.Should().NotBeNull();
        var rejectPosts = rejectMethod!.GetCustomAttributes<HttpPostAttribute>().Select(a => a.Template).ToList();
        rejectPosts.Should().Contain("{id:guid}/reject-confirmation");
    }
}
