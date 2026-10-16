using System.Reflection;
using FluentAssertions;
using MealMate.Api.Controllers.V1;
using MealMate.Application.Features.DriverDelivery.CustomerTracking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MealMate.ApiTests.Controllers;

/// <summary>
/// Wire-contract tests for the additive per-day delivery details route. The legacy
/// live-tracking and widget routes keep their exact shape; this route is independent.
/// </summary>
public class CustomerDeliveryDetailsControllerTests
{
    [Fact]
    public void Delivery_details_link_is_available_in_the_additive_contract()
    {
        typeof(CustomerDeliveryWidgetItemDto)
            .GetProperty("DeliveryDetailsUrl").Should().NotBeNull();
        typeof(CustomerDeliveryDetailsResponseDto)
            .GetProperty("Delivery").Should().NotBeNull();
    }

    [Fact]
    public void Delivery_details_contract_reuses_the_widget_item_and_keeps_the_legacy_link()
    {
        var response = typeof(CustomerDeliveryDetailsResponseDto);
        response.GetProperty(nameof(CustomerDeliveryDetailsResponseDto.ServiceDate))!.PropertyType.Should().Be(typeof(DateOnly));
        response.GetProperty(nameof(CustomerDeliveryDetailsResponseDto.ServerTimeUtc))!.PropertyType.Should().Be(typeof(DateTime));
        response.GetProperty(nameof(CustomerDeliveryDetailsResponseDto.RefreshAfterSeconds))!.PropertyType.Should().Be(typeof(int));
        response.GetProperty(nameof(CustomerDeliveryDetailsResponseDto.Delivery))!.PropertyType.Should().Be(typeof(CustomerDeliveryWidgetItemDto));

        // Additive only: the legacy stop-based DetailsUrl property is untouched.
        typeof(CustomerDeliveryWidgetItemDto).GetProperty(nameof(CustomerDeliveryWidgetItemDto.DetailsUrl)).Should().NotBeNull();
        typeof(CustomerDeliveryWidgetItemDto).GetProperty(nameof(CustomerDeliveryWidgetItemDto.DeliveryDetailsUrl))!
            .PropertyType.Should().Be(typeof(string));
    }

    [Fact]
    public void DeliveryDetails_endpoint_is_authorized_and_routed_independently()
    {
        var controller = typeof(CustomerDeliveryDetailsController);

        controller.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        controller.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        controller.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/v1/customer");

        var action = controller.GetMethod(nameof(CustomerDeliveryDetailsController.GetDeliveryDetails));
        action.Should().NotBeNull();
        action!.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("subscription-days/{subscriptionDayId:guid}/delivery-details");
        action.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();

        var dayId = action.GetParameters().Single(p => p.Name == "subscriptionDayId");
        dayId.GetCustomAttribute<FromRouteAttribute>().Should().NotBeNull();
    }
}
