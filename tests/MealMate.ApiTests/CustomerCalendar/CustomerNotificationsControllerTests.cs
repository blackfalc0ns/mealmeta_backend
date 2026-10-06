using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerNotifications;
using MealMate.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FluentAssertions;

namespace MealMate.ApiTests.CustomerCalendar;

/// <summary>
/// HTTP surface of the customer notification inbox: customer-only access and
/// command dispatch for the read endpoints.
/// </summary>
public sealed class CustomerNotificationsControllerTests
{
    [Fact]
    public async Task NonCustomer_IsForbidden()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.UserType.Returns(UserType.Restaurant);
        var controller = CreateController(currentUser, out _);

        ((await controller.GetNotifications()) as IActionResult).Should().BeOfType<ForbidResult>();
        ((await controller.MarkRead(Guid.NewGuid(), CancellationToken.None)) as IActionResult).Should().BeOfType<ForbidResult>();
        ((await controller.MarkAllRead(CancellationToken.None)) as IActionResult).Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Customer_ReadCommands_AreDispatched()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.UserType.Returns(UserType.Customer);
        var controller = CreateController(currentUser, out var sender);

        var id = Guid.NewGuid();
        await controller.MarkRead(id, CancellationToken.None);
        await sender.Received(1).Send(Arg.Is<MarkCustomerNotificationReadCommand>(c => c.NotificationId == id), Arg.Any<CancellationToken>());

        await controller.MarkAllRead(CancellationToken.None);
        await sender.Received(1).Send(Arg.Any<MarkAllCustomerNotificationsReadCommand>(), Arg.Any<CancellationToken>());

        await controller.GetNotifications(page: 2, pageSize: 5, CancellationToken.None);
        await sender.Received(1).Send(
            Arg.Is<GetCustomerNotificationsQuery>(q => q.Page == 2 && q.PageSize == 5), Arg.Any<CancellationToken>());
    }

    private static CustomerNotificationsController CreateController(ICurrentUser currentUser, out ISender sender)
    {
        sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<MarkCustomerNotificationReadCommand>(), Arg.Any<CancellationToken>())
            .Returns(MealMate.Application.Common.Models.Result.Success());
        sender.Send(Arg.Any<MarkAllCustomerNotificationsReadCommand>(), Arg.Any<CancellationToken>())
            .Returns(MealMate.Application.Common.Models.Result.Success());

        var controller = new CustomerNotificationsController(currentUser)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.ControllerContext.HttpContext.RequestServices =
            new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        return controller;
    }
}
