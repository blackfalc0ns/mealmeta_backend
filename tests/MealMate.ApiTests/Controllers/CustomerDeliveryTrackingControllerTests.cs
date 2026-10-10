using System.Reflection;
using FluentAssertions;
using MealMate.Api.Controllers.V1;
using MealMate.Application.Features.DriverDelivery.CustomerTracking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MealMate.ApiTests.Controllers;

/// <summary>
/// Wire-contract tests: the legacy live-tracking route keeps its exact shape and
/// authorization, and the new delivery-widget endpoint is customer-authorized only.
/// </summary>
public class CustomerDeliveryTrackingControllerTests
{
    [Fact]
    public void LiveTracking_route_and_authorization_are_preserved()
    {
        var controller = typeof(CustomerDeliveryTrackingController);

        controller.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        controller.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        controller.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/v1/customer/orders");

        var action = controller.GetMethod(nameof(CustomerDeliveryTrackingController.GetLiveTracking));
        action.Should().NotBeNull();
        action!.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("{orderId:guid}/live-tracking");
        action.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
    }

    [Fact]
    public void DeliveryWidget_endpoint_is_authorized_and_routed_independently()
    {
        var controller = typeof(CustomerDeliveryWidgetController);

        controller.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        controller.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        controller.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/v1/customer");

        var action = controller.GetMethod(nameof(CustomerDeliveryWidgetController.GetDeliveryWidget));
        action.Should().NotBeNull();
        action!.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("delivery-widget");

        var serviceDate = action.GetParameters().Single(p => p.Name == "serviceDate");
        serviceDate.GetCustomAttribute<FromQueryAttribute>().Should().NotBeNull();
        var subscriptionId = action.GetParameters().Single(p => p.Name == "subscriptionId");
        subscriptionId.GetCustomAttribute<FromQueryAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void Widget_contract_returns_bilingual_texts_and_nullable_evidence()
    {
        var item = typeof(CustomerDeliveryWidgetItemDto);

        item.GetProperty(nameof(CustomerDeliveryWidgetItemDto.DeliveryStatusTextAr)).Should().NotBeNull();
        item.GetProperty(nameof(CustomerDeliveryWidgetItemDto.DeliveryStatusTextEn)).Should().NotBeNull();
        item.GetProperty(nameof(CustomerDeliveryWidgetItemDto.DeliveryStatusText)).Should().NotBeNull();
        item.GetProperty(nameof(CustomerDeliveryWidgetItemDto.EtaMinutes))!.PropertyType.Should().Be(typeof(int?));
        item.GetProperty(nameof(CustomerDeliveryWidgetItemDto.ReadyAtUtc))!.PropertyType.Should().Be(typeof(DateTime?));
        item.GetProperty(nameof(CustomerDeliveryWidgetItemDto.PickedUpAtUtc))!.PropertyType.Should().Be(typeof(DateTime?));
        item.GetProperty(nameof(CustomerDeliveryWidgetItemDto.ArrivedAtUtc))!.PropertyType.Should().Be(typeof(DateTime?));
        item.GetProperty(nameof(CustomerDeliveryWidgetItemDto.DeliveredAtUtc))!.PropertyType.Should().Be(typeof(DateTime?));

        // Driver identity is bilingual data; rating must be nullable (null when no real source).
        var driver = typeof(CustomerTrackingDriverDto);
        driver.GetProperty(nameof(CustomerTrackingDriverDto.Rating))!.PropertyType.Should().Be(typeof(double?));
    }

    [Fact]
    public void Legacy_tracking_contract_keeps_its_shape()
    {
        var response = typeof(CustomerOrderTrackingResponseDto);
        response.GetProperty(nameof(CustomerOrderTrackingResponseDto.Status)).Should().NotBeNull();
        response.GetProperty(nameof(CustomerOrderTrackingResponseDto.StatusText)).Should().NotBeNull();
        response.GetProperty(nameof(CustomerOrderTrackingResponseDto.Driver)).Should().NotBeNull();
        response.GetProperty(nameof(CustomerOrderTrackingResponseDto.Location)).Should().NotBeNull();
        response.GetProperty(nameof(CustomerOrderTrackingResponseDto.Address)).Should().NotBeNull();
        response.GetProperty(nameof(CustomerOrderTrackingResponseDto.Timeline)).Should().NotBeNull();

        var timeline = typeof(CustomerTrackingTimelineDto);
        timeline.GetProperty(nameof(CustomerTrackingTimelineDto.PickedUpAtUtc)).Should().NotBeNull();
        timeline.GetProperty(nameof(CustomerTrackingTimelineDto.ArrivedAtCustomerAtUtc)).Should().NotBeNull();
        timeline.GetProperty(nameof(CustomerTrackingTimelineDto.DeliveredAtUtc)).Should().NotBeNull();
        timeline.GetProperty(nameof(CustomerTrackingTimelineDto.ProofPhotoStorageKey)).Should().NotBeNull();
    }
}
