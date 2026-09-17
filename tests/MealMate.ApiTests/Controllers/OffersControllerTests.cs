using FluentAssertions;
using MealMate.Api.Authorization;
using MealMate.Api.Controllers.Admin;
using MealMate.Api.Controllers.Customer;
using MealMate.Api.Controllers.Restaurant;
using MealMate.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using Xunit;

namespace MealMate.ApiTests.Controllers;

public class OffersControllerTests
{
    [Fact]
    public void AdminOffersController_HasAuthorizeAndManagePermission()
    {
        var authAttr = typeof(AdminOffersController).GetCustomAttribute<AuthorizeAttribute>();
        authAttr.Should().NotBeNull();

        var routeAttr = typeof(AdminOffersController).GetCustomAttribute<RouteAttribute>();
        routeAttr.Should().NotBeNull();
        routeAttr!.Template.Should().Be("api/v1/admin/offers");

        PermissionCodes.OffersManage.Should().Be("offers.manage");
    }

    [Fact]
    public void CustomerOffersController_HasCorrectRoute()
    {
        var routeAttr = typeof(CustomerOffersController).GetCustomAttribute<RouteAttribute>();
        routeAttr.Should().NotBeNull();
        routeAttr!.Template.Should().Be("api/v1/customer/offers");
    }

    [Fact]
    public void RestaurantOffersController_HasAuthorizeAndCorrectRoute()
    {
        var authAttr = typeof(RestaurantOffersController).GetCustomAttribute<AuthorizeAttribute>();
        authAttr.Should().NotBeNull();

        var routeAttr = typeof(RestaurantOffersController).GetCustomAttribute<RouteAttribute>();
        routeAttr.Should().NotBeNull();
        routeAttr!.Template.Should().Be("api/v1/restaurant/offers");
    }
}
