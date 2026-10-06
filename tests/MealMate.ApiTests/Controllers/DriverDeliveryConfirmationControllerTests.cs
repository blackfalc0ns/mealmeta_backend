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
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.Controllers;

/// <summary>
/// Contract tests for the driver confirmation endpoints (screens 07.01/07.02):
/// POST orders/{boxId}/arrive-customer and POST orders/{boxId}/deliver.
/// Success events are broadcast only for the call that actually transitioned the
/// stop (first arrival / first delivery); repeated idempotent calls and rejected
/// requests never re-broadcast. Driver-targeted events go to the driver's own
/// group, never to every DriverHub connection.
/// </summary>
public class DriverDeliveryConfirmationControllerTests
{
    private static readonly Guid BoxId = Guid.NewGuid();
    private static readonly Guid DriverProfileId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();

    private static CustomerArrivalResponseDto ArrivalResponse(bool isFirst) => new(
        BoxId: BoxId,
        BoxCode: "#BX-1256",
        CustomerName: "محمد علي",
        Status: "Arrived",
        Message: "Arrival confirmed",
        ArrivedAtUtc: new DateTime(2026, 10, 6, 7, 30, 0, DateTimeKind.Utc),
        DriverId: DriverProfileId,
        IsFirstArrival: isFirst);

    private static CompleteDeliveryResponseDto DeliveryResponse(bool isFirst) => new(
        BoxId: BoxId,
        BoxCode: "#BX-1256",
        Status: "Delivered",
        Message: "Delivered",
        DeliveredAtUtc: new DateTime(2026, 10, 6, 7, 45, 0, DateTimeKind.Utc),
        TripId: TripId,
        IsTripCompleted: true,
        RemainingStopsCount: 0,
        DriverId: DriverProfileId,
        IsFirstDelivery: isFirst);

    private static (
        DriverHomeController Controller,
        ISender Sender,
        IClientProxy DispatcherProxy,
        IClientProxy DriverProxy,
        IHubClients DriverClients) Build(Result<CustomerArrivalResponseDto>? arrivalResult = null, Result<CompleteDeliveryResponseDto>? deliveryResult = null)
    {
        var sender = Substitute.For<ISender>();
        sender.Send(
                Arg.Any<IRequest<Result<CustomerArrivalResponseDto>>>(), Arg.Any<CancellationToken>())
            .Returns(arrivalResult ?? Result.Success(ArrivalResponse(isFirst: true)));
        sender.Send(
                Arg.Any<IRequest<Result<CompleteDeliveryResponseDto>>>(), Arg.Any<CancellationToken>())
            .Returns(deliveryResult ?? Result.Success(DeliveryResponse(isFirst: true)));

        var dispatcherHub = Substitute.For<IHubContext<DispatcherHub>>();
        var dispatcherClients = Substitute.For<IHubClients>();
        var dispatcherProxy = Substitute.For<IClientProxy>();
        dispatcherHub.Clients.Returns(dispatcherClients);
        dispatcherClients.All.Returns(dispatcherProxy);

        var driverHub = Substitute.For<IHubContext<DriverHub>>();
        var driverClients = Substitute.For<IHubClients>();
        var driverProxy = Substitute.For<IClientProxy>();
        driverHub.Clients.Returns(driverClients);
        driverClients.All.Returns(driverProxy);
        driverClients.Group(Arg.Any<string>()).Returns(driverProxy);

        var services = new ServiceCollection();
        services.AddSingleton(sender);
        services.AddSingleton(dispatcherHub);
        services.AddSingleton(driverHub);
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        var controller = new DriverHomeController(dispatcherHub, driverHub)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return (controller, sender, dispatcherProxy, driverProxy, driverClients);
    }

    [Fact]
    public async Task Deliver_FirstDelivery_Broadcasts_To_Dispatcher_And_Own_Driver_Group_Only()
    {
        var (controller, _, dispatcherProxy, driverProxy, driverClients) = Build();

        var result = await controller.CompleteDelivery(
            BoxId, new CompleteDeliveryRequest { ProofPhotoStorageKey = "uploads/drivers/delivery/proof.jpg" }, null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        await dispatcherProxy.Received(1).SendCoreAsync(
            "box-delivered", Arg.Any<object[]>(), Arg.Any<CancellationToken>());

        // The driver event targets the identity-derived group, never the universal group.
        driverClients.Received(1).Group($"driver:{DriverProfileId:D}");
        await driverProxy.Received(1).SendCoreAsync(
            DriverHub.EventDeliveryCompleted, Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deliver_Repeat_IsIdempotent_And_Broadcasts_Nothing()
    {
        var (controller, _, dispatcherProxy, driverProxy, _) = Build(
            deliveryResult: Result.Success(DeliveryResponse(isFirst: false)));

        var result = await controller.CompleteDelivery(
            BoxId, new CompleteDeliveryRequest { ProofPhotoStorageKey = "uploads/drivers/delivery/proof.jpg" }, null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await dispatcherProxy.DidNotReceiveWithAnyArgs().SendCoreAsync(default!, default!, default);
        await driverProxy.DidNotReceiveWithAnyArgs().SendCoreAsync(default!, default!, default);
    }

    [Fact]
    public async Task Deliver_Rejected_Sends_No_Events()
    {
        var (controller, _, dispatcherProxy, driverProxy, _) = Build(
            deliveryResult: Result.Failure<CompleteDeliveryResponseDto>(Error.Validation(
                "Orders.ProofPhotoRequired", "A photo documenting box delivery is required.")));

        var result = await controller.CompleteDelivery(BoxId, new CompleteDeliveryRequest(), null, CancellationToken.None);

        var problem = result as ObjectResult;
        problem!.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        ((ProblemDetails)problem.Value!).Extensions["code"].Should().Be("Orders.ProofPhotoRequired");

        await dispatcherProxy.DidNotReceiveWithAnyArgs().SendCoreAsync(default!, default!, default);
        await driverProxy.DidNotReceiveWithAnyArgs().SendCoreAsync(default!, default!, default);
    }

    [Fact]
    public async Task Deliver_Response_Carries_Trip_Completion_Fields()
    {
        var (controller, _, _, _, _) = Build();

        var result = await controller.CompleteDelivery(
            BoxId, new CompleteDeliveryRequest { ProofPhotoStorageKey = "uploads/drivers/delivery/proof.jpg" }, null, CancellationToken.None);

        var ok = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(ok.Value!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var root = JsonDocument.Parse(json).RootElement;

        root.GetProperty("tripId").GetGuid().Should().Be(TripId);
        root.GetProperty("isTripCompleted").GetBoolean().Should().BeTrue();
        root.GetProperty("remainingStopsCount").GetInt32().Should().Be(0);
        root.GetProperty("isFirstDelivery").GetBoolean().Should().BeTrue();
        root.GetProperty("deliveredAtUtc").GetDateTime().Should().Be(new DateTime(2026, 10, 6, 7, 45, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Arrive_FirstArrival_Broadcasts_To_Dispatcher_And_Own_Driver_Group()
    {
        var (controller, _, dispatcherProxy, driverProxy, driverClients) = Build();

        var result = await controller.ArriveAtCustomer(BoxId, null, null, null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        await dispatcherProxy.Received(1).SendCoreAsync(
            "driver-arrived-at-customer", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
        driverClients.Received(1).Group($"driver:{DriverProfileId:D}");
        await driverProxy.Received(1).SendCoreAsync(
            "driver-arrived-at-customer", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Arrive_Repeat_IsIdempotent_And_Broadcasts_Nothing()
    {
        var (controller, _, dispatcherProxy, driverProxy, _) = Build(
            arrivalResult: Result.Success(ArrivalResponse(isFirst: false)));

        var result = await controller.ArriveAtCustomer(BoxId, null, null, null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await dispatcherProxy.DidNotReceiveWithAnyArgs().SendCoreAsync(default!, default!, default);
        await driverProxy.DidNotReceiveWithAnyArgs().SendCoreAsync(default!, default!, default);
    }

    [Fact]
    public async Task Arrive_Response_Carries_Stable_First_Arrival_Contract()
    {
        var (controller, _, _, _, _) = Build();

        var result = await controller.ArriveAtCustomer(BoxId, null, null, null, CancellationToken.None);

        var ok = (OkObjectResult)result;
        var json = JsonSerializer.Serialize(ok.Value!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var root = JsonDocument.Parse(json).RootElement;

        root.GetProperty("boxId").GetGuid().Should().Be(BoxId);
        root.GetProperty("arrivedAtUtc").GetDateTime().Should().Be(new DateTime(2026, 10, 6, 7, 30, 0, DateTimeKind.Utc));
        root.GetProperty("isFirstArrival").GetBoolean().Should().BeTrue();
        root.GetProperty("driverId").GetGuid().Should().Be(DriverProfileId);
        // Containment: no customer phone anywhere in the arrival payload.
        json.Should().NotContain("customerPhone");
    }
}
