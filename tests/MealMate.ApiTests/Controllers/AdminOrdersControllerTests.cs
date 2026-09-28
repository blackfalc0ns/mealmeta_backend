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
}
