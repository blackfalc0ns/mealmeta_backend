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
    public void CustomerOffersController_GetSavedOffers_HasAuthorizeHttpGetAndSaveRoutes()
    {
        var save = typeof(CustomerOffersController).GetMethod(nameof(CustomerOffersController.SaveOffer));
        save.Should().NotBeNull();
        save!.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        save.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("{offerId:guid}/save");

        var unsave = typeof(CustomerOffersController).GetMethod(nameof(CustomerOffersController.UnsaveOffer));
        unsave.Should().NotBeNull();
        unsave!.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        unsave.GetCustomAttribute<HttpDeleteAttribute>()!.Template.Should().Be("{offerId:guid}/save");

        var saved = typeof(CustomerOffersController).GetMethod(nameof(CustomerOffersController.GetSavedOffers));
        saved.Should().NotBeNull();
        saved!.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        saved.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("saved");
    }

    [Fact]
    public void SavedOfferNotSavedMessage_ResolvesInBothLanguagesAndSwitchesBack()
    {
        var manager = new System.Resources.ResourceManager("MealMate.Api.Resources.Resources.Messages", typeof(MealMate.Api.Resources.Messages).Assembly);
        var arabic = manager.GetString("offer.not_saved", System.Globalization.CultureInfo.GetCultureInfo("ar"));
        var english = manager.GetString("offer.not_saved", System.Globalization.CultureInfo.GetCultureInfo("en"));
        Assert.False(string.IsNullOrWhiteSpace(arabic));
        Assert.False(string.IsNullOrWhiteSpace(english));
        Assert.Equal(arabic, manager.GetString("offer.not_saved", System.Globalization.CultureInfo.GetCultureInfo("ar")));
        Assert.NotEqual(arabic, english);
    }

    [Fact]
    public void CustomerOffersController_CalendarEndpoints_HaveAuthorizeAndRoutes()
    {
        var addToCalendar = typeof(CustomerOffersController).GetMethod(nameof(CustomerOffersController.AddOfferToCalendar));
        addToCalendar.Should().NotBeNull();
        addToCalendar!.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        addToCalendar!.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("{offerId:guid}/calendar");

        var removeFromCalendar = typeof(CustomerOffersController).GetMethod(nameof(CustomerOffersController.RemoveOfferFromCalendar));
        removeFromCalendar.Should().NotBeNull();
        removeFromCalendar!.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        removeFromCalendar!.GetCustomAttribute<HttpDeleteAttribute>()!.Template.Should().Be("{offerId:guid}/calendar/{date}");

        var calendar = typeof(CustomerOffersController).GetMethod(nameof(CustomerOffersController.GetOfferCalendar));
        calendar.Should().NotBeNull();
        calendar!.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        calendar!.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("calendar");
    }

    [Fact]
    public void OfferCalendarNotBookedMessage_ResolvesInBothLanguages()
    {
        var manager = new System.Resources.ResourceManager("MealMate.Api.Resources.Resources.Messages", typeof(MealMate.Api.Resources.Messages).Assembly);
        var arabic = manager.GetString("offer.calendar_not_booked", System.Globalization.CultureInfo.GetCultureInfo("ar"));
        var english = manager.GetString("offer.calendar_not_booked", System.Globalization.CultureInfo.GetCultureInfo("en"));
        Assert.False(string.IsNullOrWhiteSpace(arabic));
        Assert.False(string.IsNullOrWhiteSpace(english));
        Assert.Equal(arabic, manager.GetString("offer.calendar_not_booked", System.Globalization.CultureInfo.GetCultureInfo("ar")));
        Assert.NotEqual(arabic, english);
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

    [Fact]
    public void CustomerOffersController_GetEligibleRestaurants_HasAuthorizeAndRoute()
    {
        var method = typeof(CustomerOffersController).GetMethod(nameof(CustomerOffersController.GetEligibleRestaurants));
        method.Should().NotBeNull();

        method!.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        method.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("{offerId:guid}/eligible-restaurants");
    }
}
