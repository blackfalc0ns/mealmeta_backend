using System.Text.Json;
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
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.Controllers;

/// <summary>
/// Contract tests for the supervisor shift-status endpoint's realtime side:
/// driver-status-updated reaches only the affected driver's own group, only
/// after the change is saved, and the payload keys match the mobile contract.
/// </summary>
public class DispatcherShiftStatusControllerTests
{
    [Fact]
    public async Task UpdateDriverShiftStatus_OnSuccess_BroadcastsToOwnDriverGroupAndDispatchers()
    {
        // Arrange
        var driverId = Guid.NewGuid();
        var response = new UpdateDispatcherDriverShiftStatusResponseDto(
            driverId, "Active", true, "Online and available for new orders", DateTime.UtcNow);

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<IRequest<Result<UpdateDispatcherDriverShiftStatusResponseDto>>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(response));

        var dispatcherHub = Substitute.For<IHubContext<DispatcherHub>>();
        var dispatcherClients = Substitute.For<IHubClients>();
        var dispatcherProxy = Substitute.For<ISingleClientProxy>();
        dispatcherHub.Clients.Returns(dispatcherClients);
        dispatcherClients.Group(Arg.Any<string>()).Returns(dispatcherProxy);

        var driverHub = Substitute.For<IHubContext<DriverHub>>();
        var driverClients = Substitute.For<IHubClients>();
        var driverProxy = Substitute.For<ISingleClientProxy>();
        driverHub.Clients.Returns(driverClients);
        driverClients.Group(Arg.Any<string>()).Returns(driverProxy);

        var controller = BuildController(sender, dispatcherHub, driverHub);

        // Act
        var result = await controller.UpdateDriverShiftStatus(
            driverId, new UpdateDriverShiftStatusRequest("Active"), CancellationToken.None);

        // Assert — the event went to the driver's identity-derived group only
        result.Should().BeOfType<OkObjectResult>();
        driverClients.Received(1).Group($"driver:{driverId:D}");

        await driverProxy.Received(1).SendCoreAsync(
            DriverHub.EventDriverStatusUpdated,
            Arg.Is<object[]>(args => args.Length == 1 && args[0] is UpdateDispatcherDriverShiftStatusResponseDto),
            Arg.Any<CancellationToken>());

        // Dispatcher dashboard keeps its existing event name (restaurant + universal groups)
        await dispatcherProxy.Received(2).SendCoreAsync(
            DispatcherHub.EventDriverStatusUpdated,
            Arg.Is<object[]>(args => args.Length == 1 && args[0] is UpdateDispatcherDriverShiftStatusResponseDto),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateDriverShiftStatus_OnFailure_SendsNoEvent()
    {
        // Arrange
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<IRequest<Result<UpdateDispatcherDriverShiftStatusResponseDto>>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<UpdateDispatcherDriverShiftStatusResponseDto>(
                Error.Conflict("DRIVER_SHIFT_ACTIVE_ORDERS", "Active deliveries remain.")));

        var dispatcherHub = Substitute.For<IHubContext<DispatcherHub>>();
        var driverHub = Substitute.For<IHubContext<DriverHub>>();
        var driverClients = Substitute.For<IHubClients>();
        var driverProxy = Substitute.For<ISingleClientProxy>();
        driverHub.Clients.Returns(driverClients);
        driverClients.Group(Arg.Any<string>()).Returns(driverProxy);

        var controller = BuildController(sender, dispatcherHub, driverHub);

        // Act
        var result = await controller.UpdateDriverShiftStatus(
            Guid.NewGuid(), new UpdateDriverShiftStatusRequest("Inactive"), CancellationToken.None);

        // Assert — no broadcast on failed operations
        result.Should().BeOfType<ObjectResult>();
        await driverProxy.DidNotReceiveWithAnyArgs().SendCoreAsync(default!, default!, default);
    }

    [Fact]
    public void DriverStatusUpdatedPayload_UsesMobileContractKeys()
    {
        // The mobile app reads driverId, shiftStatus, isAvailable, currentStatusText, updatedAtUtc.
        var payload = new UpdateDispatcherDriverShiftStatusResponseDto(
            Guid.NewGuid(), "Inactive", false, "غير متصل", new DateTime(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc));

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("driverId").ValueKind.Should().Be(JsonValueKind.String);
        root.GetProperty("shiftStatus").GetString().Should().Be("Inactive");
        root.GetProperty("isAvailable").GetBoolean().Should().BeFalse();
        root.GetProperty("currentStatusText").GetString().Should().Be("غير متصل");
        root.GetProperty("updatedAtUtc").ValueKind.Should().Be(JsonValueKind.String);
        root.EnumerateObject().Select(p => p.Name).Should().HaveCount(5);
    }

    private static DispatcherDashboardController BuildController(
        ISender sender,
        IHubContext<DispatcherHub> dispatcherHub,
        IHubContext<DriverHub> driverHub)
    {
        var services = new ServiceCollection();
        services.AddSingleton(sender);
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("mm_restaurant_id", Guid.NewGuid().ToString()),
            }, "TestAuth"));

        return new DispatcherDashboardController(
            dispatcherHub,
            Substitute.For<IGoogleRoutingService>(),
            new DriverLocationBroadcaster(dispatcherHub),
            driverHub)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }
}
