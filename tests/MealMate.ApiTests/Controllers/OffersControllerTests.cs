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
    public void CustomerOffersController_GetAvailableOffers_HasAllowAnonymousAndHttpGet()
    {
        var method = typeof(CustomerOffersController).GetMethod(nameof(CustomerOffersController.GetAvailableOffers));
        method.Should().NotBeNull();

        var allowAnonymous = method!.GetCustomAttribute<AllowAnonymousAttribute>();
        allowAnonymous.Should().NotBeNull();

        var httpGet = method.GetCustomAttribute<HttpGetAttribute>();
        httpGet.Should().NotBeNull();
        httpGet!.Template.Should().Be("available");
    }

    [Fact]
    public void CustomerOffersController_GetAvailableOffersPost_HasAllowAnonymousAndHttpPost()
    {
        var method = typeof(CustomerOffersController).GetMethod(nameof(CustomerOffersController.GetAvailableOffersPost));
        method.Should().NotBeNull();

        var allowAnonymous = method!.GetCustomAttribute<AllowAnonymousAttribute>();
        allowAnonymous.Should().NotBeNull();

        var httpPost = method.GetCustomAttribute<HttpPostAttribute>();
        httpPost.Should().NotBeNull();
        httpPost!.Template.Should().Be("available");
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
