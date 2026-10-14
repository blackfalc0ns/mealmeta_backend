using MealMate.Api.Controllers.Customer;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace MealMate.ApiTests.Controllers;

public sealed class CampaignPaymentControllerTests
{
    [Fact]
    public async Task StartAndStatusUseSessionRouteAndAuthenticatedCustomerIdentity()
    {
        var customerId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var user = Substitute.For<ICurrentUser>();
        user.UserType.Returns(UserType.Customer);
        user.UserId.Returns(customerId);
        var payments = Substitute.For<ICampaignPaymentService>();
        var request = new StartCampaignPaymentRequest("payment-key", "https://app.example/return", "https://app.example/error");
        var expires = DateTime.UtcNow.AddMinutes(15);
        payments.StartAsync(sessionId, customerId, request, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new CampaignPaymentStartedDto(sessionId, "pz-track", "https://pay.example/", 10m, "KWD", "Pending", expires)));
        payments.GetStatusAsync(sessionId, customerId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new CampaignPaymentStatusDto(sessionId, "pz-track", "Paid", true, false, 10m, "KWD", expires)));
        var controller = new CustomerCollaborativeCampaignsController(user, payments);

        var started = Assert.IsType<OkObjectResult>(await controller.StartPayment(sessionId, request, CancellationToken.None));
        Assert.Equal(sessionId, Assert.IsType<CampaignPaymentStartedDto>(started.Value).SessionId);
        var status = Assert.IsType<OkObjectResult>(await controller.GetPaymentStatus(sessionId, CancellationToken.None));
        Assert.True(Assert.IsType<CampaignPaymentStatusDto>(status.Value).IsPaid);
        await payments.Received(1).StartAsync(sessionId, customerId, request, Arg.Any<CancellationToken>());
        await payments.Received(1).GetStatusAsync(sessionId, customerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestaurantActorCannotInitiateOrReadCustomerCampaignPayment()
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserType.Returns(UserType.Restaurant);
        user.UserId.Returns(Guid.NewGuid());
        var payments = Substitute.For<ICampaignPaymentService>();
        var controller = new CustomerCollaborativeCampaignsController(user, payments);
        Assert.IsType<ForbidResult>(await controller.StartPayment(Guid.NewGuid(), new StartCampaignPaymentRequest("key"), CancellationToken.None));
        Assert.IsType<ForbidResult>(await controller.GetPaymentStatus(Guid.NewGuid(), CancellationToken.None));
        await payments.DidNotReceiveWithAnyArgs().StartAsync(default, default, default!, default);
        await payments.DidNotReceiveWithAnyArgs().GetStatusAsync(default, default, default);
    }
}
