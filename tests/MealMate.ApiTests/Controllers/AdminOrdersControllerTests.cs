using System.Reflection;
using FluentAssertions;
using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.AdminOrders;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.Controllers;

public class AdminOrdersControllerTests
{
    [Fact]
    public void Controller_RequiresAuthorization_And_HasNoAllowAnonymous()
    {
        var authAttr = typeof(AdminOrdersController).GetCustomAttribute<AuthorizeAttribute>();
        authAttr.Should().NotBeNull("AdminOrdersController must require authorization.");

        var allowAnon = typeof(AdminOrdersController).GetCustomAttribute<AllowAnonymousAttribute>();
        allowAnon.Should().BeNull("AdminOrdersController must not allow anonymous access.");
    }

    [Fact]
    public void Controller_HasAdminPolicy_AndCorrectRoute()
    {
        var type = typeof(AdminOrdersController);

        var authAttr = type.GetCustomAttribute<AuthorizeAttribute>();
        authAttr.Should().NotBeNull();
        authAttr!.Policy.Should().Be("AdminPolicy", "Admin orders endpoints must be protected by AdminPolicy.");

        var routeAttr = type.GetCustomAttribute<RouteAttribute>();
        routeAttr.Should().NotBeNull();
        routeAttr!.Template.Should().Be("api/v1/admin/orders");
    }

    [Fact]
    public void GetConfirmationOverdue_HasExpectedRouteAndMethod()
    {
        var method = typeof(AdminOrdersController).GetMethod(nameof(AdminOrdersController.GetConfirmationOverdue));
        method.Should().NotBeNull();

        var httpGetAttr = method!.GetCustomAttribute<HttpGetAttribute>();
        httpGetAttr.Should().NotBeNull();
        httpGetAttr!.Template.Should().Be("confirmation-overdue");
    }

    [Fact]
    public async Task GetConfirmationOverdue_DispatchesQuery_AndReturnsOk()
    {
        // Arrange
        var sender = Substitute.For<ISender>();
        var items = new List<OverdueOrderDto>
        {
            new(
                OrderId: Guid.NewGuid(),
                RestaurantId: Guid.NewGuid(),
                RestaurantNameAr: "مطعم البركة",
                RestaurantNameEn: "Baraka Restaurant",
                SubscriptionDayId: Guid.NewGuid(),
                ServiceDate: new DateOnly(2026, 9, 28),
                BoxCount: 2,
                DispatchedAtUtc: DateTime.UtcNow.AddMinutes(-40),
                OverdueMinutes: 40)
        };
        var pagedResult = new PagedResult<OverdueOrderDto>(items, 1, 1, 20);

        sender.Send(Arg.Any<GetConfirmationOverdueOrdersQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(pagedResult));

        var services = new ServiceCollection();
        services.AddSingleton(sender);
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };

        var controller = new AdminOrdersController
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        // Act
        var actionResult = await controller.GetConfirmationOverdue(pageNumber: 1, pageSize: 20, CancellationToken.None);

        // Assert
        var okResult = actionResult as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);

        var value = okResult.Value as PagedResult<OverdueOrderDto>;
        value.Should().NotBeNull();
        value!.TotalCount.Should().Be(1);
        value.Items.Should().HaveCount(1);
        value.Items.First().RestaurantNameEn.Should().Be("Baraka Restaurant");

        await sender.Received(1).Send(
            Arg.Is<GetConfirmationOverdueOrdersQuery>(q => q.PageNumber == 1 && q.PageSize == 20),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ManualReassign_DispatchesCommand_AndReturnsOk()
    {
        // Arrange
        var sender = Substitute.For<ISender>();
        var orderId = Guid.NewGuid();
        var targetRestId = Guid.NewGuid();
        var outcome = new MealMate.Application.Features.CustomerCheckout.ReassignmentOutcome(
            ErrorCode: null,
            DayId: Guid.NewGuid(),
            RestaurantId: targetRestId,
            MenuPlanVersionId: Guid.NewGuid(),
            Status: MealMate.Domain.Enums.SubscriptionDayStatus.ReplacementWindowOpen);

        sender.Send(Arg.Any<ManualReassignOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(outcome));

        var services = new ServiceCollection();
        services.AddSingleton(sender);
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var controller = new AdminOrdersController { ControllerContext = new ControllerContext { HttpContext = httpContext } };

        // Act
        var result = await controller.ManualReassign(orderId, new ManualReassignOrderRequest(targetRestId, "Kitchen delay"), CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);

        await sender.Received(1).Send(
            Arg.Is<ManualReassignOrderCommand>(c => c.OrderId == orderId && c.TargetRestaurantId == targetRestId && c.Reason == "Kitchen delay"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenReplacementWindow_DispatchesCommand_AndReturnsOk()
    {
        // Arrange
        var sender = Substitute.For<ISender>();
        var orderId = Guid.NewGuid();
        var response = new OpenReplacementWindowResponseDto(orderId, DateTime.UtcNow, DateTime.UtcNow.AddHours(24));

        sender.Send(Arg.Any<OpenReplacementWindowCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(response));

        var services = new ServiceCollection();
        services.AddSingleton(sender);
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var controller = new AdminOrdersController { ControllerContext = new ControllerContext { HttpContext = httpContext } };

        // Act
        var result = await controller.OpenReplacementWindow(orderId, new OpenReplacementWindowOrderRequest("Manual trigger", true), CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);

        await sender.Received(1).Send(
            Arg.Is<OpenReplacementWindowCommand>(c => c.OrderId == orderId && c.Reason == "Manual trigger" && c.NotifyCustomer),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyException_DispatchesCommand_AndReturnsOk()
    {
        // Arrange
        var sender = Substitute.For<ISender>();
        var orderId = Guid.NewGuid();
        var response = new OrderExceptionResponseDto(orderId, Guid.NewGuid(), DateTime.UtcNow);

        sender.Send(Arg.Any<ApplyOrderExceptionCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(response));

        var services = new ServiceCollection();
        services.AddSingleton(sender);
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var controller = new AdminOrdersController { ControllerContext = new ControllerContext { HttpContext = httpContext } };

        // Act
        var result = await controller.ApplyException(orderId, new ApplyOrderExceptionRequest("change_restaurant", "Customer VIP request"), CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);

        await sender.Received(1).Send(
            Arg.Is<ApplyOrderExceptionCommand>(c => c.OrderId == orderId && c.ExceptionType == "change_restaurant" && c.Reason == "Customer VIP request"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetReplacementWindows_DispatchesQuery_AndReturnsOk()
    {
        // Arrange
        var sender = Substitute.For<ISender>();
        var response = new ReplacementWindowListResponseDto(new List<ReplacementWindowRowDto>(), 0);

        sender.Send(Arg.Any<GetReplacementWindowsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(response));

        var services = new ServiceCollection();
        services.AddSingleton(sender);
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var controller = new AdminOrdersController { ControllerContext = new ControllerContext { HttpContext = httpContext } };

        // Act
        var result = await controller.GetReplacementWindows(CancellationToken.None);

        // Assert
        var okResult = result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(200);

        await sender.Received(1).Send(
            Arg.Any<GetReplacementWindowsQuery>(),
            Arg.Any<CancellationToken>());
    }
}
