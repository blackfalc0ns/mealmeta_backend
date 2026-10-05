using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using MealMate.Api.Controllers.V1;
using MealMate.Api.Realtime;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
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
/// Contract tests for the driver home screen and the self-service shift closure.
/// The operational status is owned by the delivery supervisor: the driver app only
/// reads it (GET home) and must not be able to change it through shift/start or shift/end.
/// </summary>
public class DriverHomeControllerTests
{
    private static readonly Guid DriverId = Guid.NewGuid();

    private static DriverHomeScreenResponse InactiveHomeResponse() => new(
        DriverId: DriverId,
        DriverName: "أحمد إبراهيم",
        DriverCode: "DRV-1256",
        ProfileImageUrl: null,
        ShiftStatus: "Inactive",
        IsAvailable: false,
        CurrentStatusText: "غير متصل",
        NextLocationText: null,
        TargetProgress: null,
        CurrentDeliveryTask: null,
        TodaySummary: null,
        DailyPerformance: null,
        ActiveSession: null,
        UnreadNotificationsCount: 0);

    private static (DriverHomeController Controller, ISender Sender) CreateController(
        ClaimsPrincipal? user = null,
        Result<DriverHomeScreenResponse>? homeResult = null)
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<IRequest<Result<DriverHomeScreenResponse>>>(), Arg.Any<CancellationToken>())
            .Returns(homeResult ?? Result.Success(InactiveHomeResponse()));

        var services = new ServiceCollection();
        services.AddSingleton(sender);
        services.AddSingleton(Substitute.For<IHubContext<DispatcherHub>>());
        services.AddSingleton(Substitute.For<IHubContext<DriverHub>>());

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (user is not null)
        {
            httpContext.User = user;
        }

        var controller = new DriverHomeController(
            Substitute.For<IHubContext<DispatcherHub>>(),
            Substitute.For<IHubContext<DriverHub>>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return (controller, sender);
    }

    private static ClaimsPrincipal PrincipalWithRoles(params string[] roles) => new(
        new ClaimsIdentity(
            roles.Select(r => new Claim(ClaimTypes.Role, r)).Append(new Claim("sub", Guid.NewGuid().ToString())),
            "TestAuth"));

    [Fact]
    public async Task GetHomeScreen_InactiveResponse_KeepsAllContractKeysIncludingNulls()
    {
        // The mobile screen switches on shiftStatus and reads the six operational
        // fields; every key must survive serialization even when its value is null.
        var (controller, _) = CreateController();

        var result = await controller.GetHomeScreen(null, CancellationToken.None);

        var ok = result as OkObjectResult;
        ok.Should().NotBeNull();
        ok!.StatusCode.Should().Be((int)HttpStatusCode.OK);

        var json = JsonSerializer.Serialize(ok.Value!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("driverId").GetGuid().Should().Be(DriverId);
        root.GetProperty("driverName").GetString().Should().Be("أحمد إبراهيم");
        root.GetProperty("shiftStatus").GetString().Should().Be("Inactive");
        root.GetProperty("isAvailable").GetBoolean().Should().BeFalse();
        root.GetProperty("currentStatusText").GetString().Should().Be("غير متصل");
        root.GetProperty("unreadNotificationsCount").GetInt32().Should().Be(0);

        root.GetProperty("profileImageUrl").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("nextLocationText").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("targetProgress").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("currentDeliveryTask").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("todaySummary").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("dailyPerformance").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("activeSession").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ShiftStart_ForDriverRole_ReturnsForbidden()
    {
        // The supervisor owns operational status; the driver role cannot self-start a shift.
        var (controller, sender) = CreateController(user: PrincipalWithRoles(AppRoles.Driver));

        var result = await controller.StartShift(new StartShiftRequest(), null, CancellationToken.None);

        var problem = result as ObjectResult;
        problem.Should().NotBeNull("driver role must receive 403 ProblemDetails, not the home payload");
        problem!.StatusCode.Should().Be((int)HttpStatusCode.Forbidden);
        await sender.DidNotReceive().Send(Arg.Any<IRequest<Result<DriverHomeScreenResponse>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShiftEnd_ForDriverRole_ReturnsForbidden()
    {
        var (controller, sender) = CreateController(user: PrincipalWithRoles(AppRoles.Driver));

        var result = await controller.EndShift(new EndShiftRequest(), null, CancellationToken.None);

        var problem = result as ObjectResult;
        problem.Should().NotBeNull();
        problem!.StatusCode.Should().Be((int)HttpStatusCode.Forbidden);
        await sender.DidNotReceive().Send(Arg.Any<IRequest<Result<DriverShiftSummaryResponse>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShiftStart_ForDeliveryManagerRole_StillReachesCommand()
    {
        // Closure targets the driver self-service path only; dashboard roles keep access.
        var (controller, sender) = CreateController(user: PrincipalWithRoles(AppRoles.DeliveryManager));

        var result = await controller.StartShift(new StartShiftRequest(), null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await sender.Received(1).Send(Arg.Any<IRequest<Result<DriverHomeScreenResponse>>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(AppRoles.Customer)]
    [InlineData("UnrelatedRole")]
    public async Task ShiftStart_WithoutSupervisorRole_ReturnsForbidden(string role)
    {
        var (controller, sender) = CreateController(user: PrincipalWithRoles(role));
        var result = await controller.StartShift(new StartShiftRequest(), null, CancellationToken.None);
        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        await sender.DidNotReceive().Send(Arg.Any<IRequest<Result<DriverHomeScreenResponse>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DispatcherController_ExposesShiftStatusEndpoint_WithPatchRoute()
    {
        var method = typeof(DispatcherDashboardController).GetMethod("UpdateDriverShiftStatus");
        method.Should().NotBeNull("PATCH /api/v1/dispatcher/drivers/{driverId}/shift-status must exist");

        var patch = method!.GetCustomAttribute<HttpPatchAttribute>();
        patch.Should().NotBeNull();
        patch!.Template.Should().Be("drivers/{driverId:guid}/shift-status");

        var bodyParam = method.GetParameters().Single(p => p.Name == "request");
        bodyParam.GetCustomAttribute<FromBodyAttribute>().Should().NotBeNull();
    }
}
