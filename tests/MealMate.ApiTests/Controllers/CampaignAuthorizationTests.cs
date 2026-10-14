using System.Reflection;
using FluentAssertions;
using MealMate.Api.Authorization;
using MealMate.Api.Controllers.Admin;
using MealMate.Api.Controllers.Customer;
using MealMate.Api.Controllers.Restaurant;
using MealMate.Api.Controllers.Webhooks;
using MealMate.Api.Localization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.Controllers;

public class CampaignAuthorizationTests
{
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IConfiguration _config = Substitute.For<IConfiguration>();

    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_sender);
        services.AddSingleton(_currentUser);
        services.AddSingleton(_config);
        services.AddSingleton(new ProblemDetailsLocalizer(Substitute.For<ILocalizedMessageProvider>()));
        return services.BuildServiceProvider();
    }

    private TController InitController<TController>(TController controller, Action<DefaultHttpContext>? configureHttp = null)
        where TController : ControllerBase
    {
        var httpContext = new DefaultHttpContext { RequestServices = CreateServices() };
        configureHttp?.Invoke(httpContext);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public void AdminController_HasAuthorizeAndCampaignPermissionAttributes()
    {
        var controllerType = typeof(AdminCollaborativeCampaignsController);
        var authAttr = controllerType.GetCustomAttribute<AuthorizeAttribute>();
        authAttr.Should().NotBeNull("Admin controller must require authorization.");

        var methods = new[]
        {
            nameof(AdminCollaborativeCampaignsController.Create),
            nameof(AdminCollaborativeCampaignsController.Transition),
            nameof(AdminCollaborativeCampaignsController.Stop),
            nameof(AdminCollaborativeCampaignsController.End),
            nameof(AdminCollaborativeCampaignsController.OverrideProfitability),
            nameof(AdminCollaborativeCampaignsController.ReduceCapacity),
            nameof(AdminCollaborativeCampaignsController.ReviewCapacityRequest),
        };

        foreach (var methodName in methods)
        {
            var method = controllerType.GetMethod(methodName);
            method.Should().NotBeNull($"Method {methodName} must exist on admin controller.");
            var permAttr = method!.GetCustomAttribute<HasPermissionAttribute>();
            permAttr.Should().NotBeNull($"Method {methodName} must have [HasPermission].");
            permAttr!.Policy.Should().Contain(PermissionCodes.CampaignsManage);
        }
    }

    [Fact]
    public async Task RestaurantController_WhenCallerNotRestaurantTenant_ReturnsForbid()
    {
        // Caller is a customer, not restaurant
        _currentUser.UserType.Returns(UserType.Customer);
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.RestaurantId.Returns((Guid?)null);

        var controller = InitController(new RestaurantCollaborativeCampaignsController(_currentUser, NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<RestaurantCollaborativeCampaignsController>>()));
        var campaignId = Guid.NewGuid();
        var command = new SubmitCapacityChangeRequestCommand(
            campaignId,
            Guid.NewGuid(),
            new DateOnly(2026, 10, 20),
            30,
            "Increase capacity");

        // Act
        var result = await controller.RequestCapacityChange(campaignId, command, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ForbidResult>();
        await _sender.DidNotReceive().Send(Arg.Any<IRequest<Result<CapacityChangeRequestDto>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestaurantController_WhenCallerIsRestaurantTenant_ExecutesSuccessfully()
    {
        var restId = Guid.NewGuid();
        _currentUser.UserType.Returns(UserType.Restaurant);
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.RestaurantId.Returns(restId);

        var controller = InitController(new RestaurantCollaborativeCampaignsController(_currentUser, NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<RestaurantCollaborativeCampaignsController>>()));
        var campaignId = Guid.NewGuid();
        var command = new SubmitCapacityChangeRequestCommand(
            campaignId,
            restId,
            new DateOnly(2026, 10, 20),
            30,
            "Increase capacity");

        var expectedDto = new CapacityChangeRequestDto(Guid.NewGuid(), campaignId, restId, command.ServiceDate, 30, "PendingReview", null);
        _sender.Send(command, Arg.Any<CancellationToken>())
            .Returns(Result.Success(expectedDto));

        // Act
        var result = await controller.RequestCapacityChange(campaignId, command, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(expectedDto);
    }

    [Fact]
    public async Task CustomerController_WhenCallerNotCustomer_ReturnsForbidOnCancelSubscription()
    {
        _currentUser.UserType.Returns(UserType.Driver);
        _currentUser.UserId.Returns(Guid.NewGuid());

        var controller = InitController(new CustomerCollaborativeCampaignsController(_currentUser));
        var subId = Guid.NewGuid();
        var command = new CancelCampaignSubscriptionCommand(subId, "Reason");

        // Act
        var result = await controller.CancelSubscription(subId, command, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ForbidResult>();
        await _sender.DidNotReceive().Send(Arg.Any<IRequest<Result<CancelCampaignSubscriptionResultDto>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WebhookController_WhenMissingOrInvalidSecret_ReturnsUnauthorized()
    {
        _config["PaymentGateways:CampaignWebhookSecret"].Returns("correct-secret-123");

        var controller = InitController(new CampaignPaymentWebhookController(_config), http =>
        {
            // Missing X-Webhook-Secret header
        });

        var command = new ProcessPaymentCallbackCommand(
            Guid.NewGuid(), "Payzah", "TXN-1", null, 50m, "KWD", true);

        // Act
        var result = await controller.ProcessCallback(command, CancellationToken.None);

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
        await _sender.DidNotReceive().Send(Arg.Any<IRequest<Result<ProcessPaymentCallbackResultDto>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WebhookController_WhenValidSecret_ExecutesSuccessfully()
    {
        _config["PaymentGateways:CampaignWebhookSecret"].Returns("correct-secret-123");

        var controller = InitController(new CampaignPaymentWebhookController(_config), http =>
        {
            http.Request.Headers["X-Webhook-Secret"] = "correct-secret-123";
        });

        var command = new ProcessPaymentCallbackCommand(
            Guid.NewGuid(), "Payzah", "TXN-1", null, 50m, "KWD", true);

        var expectedDto = new ProcessPaymentCallbackResultDto(command.CheckoutSessionId, Guid.NewGuid(), "Succeeded", "Paid");
        _sender.Send(command, Arg.Any<CancellationToken>())
            .Returns(Result.Success(expectedDto));

        // Act
        var result = await controller.ProcessCallback(command, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(expectedDto);
    }

    [Fact]
    public async Task RestaurantController_GetActiveCampaigns_WhenQueryRestaurantDiffersFromToken_ReturnsForbid()
    {
        _currentUser.UserType.Returns(UserType.Restaurant);
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.RestaurantId.Returns(Guid.NewGuid());

        var controller = InitController(new RestaurantCollaborativeCampaignsController(
            _currentUser, NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<RestaurantCollaborativeCampaignsController>>()));

        var result = await controller.GetActiveCampaigns(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        await _sender.DidNotReceive().Send(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestaurantController_GetInvitations_WhenTokenHasNoRestaurant_ReturnsForbid()
    {
        _currentUser.UserType.Returns(UserType.Customer);
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.RestaurantId.Returns((Guid?)null);

        var controller = InitController(new RestaurantCollaborativeCampaignsController(
            _currentUser, NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<RestaurantCollaborativeCampaignsController>>()));

        var result = await controller.GetInvitations(null, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        await _sender.DidNotReceive().Send(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestaurantController_ReduceCapacity_WhenBodyRestaurantDiffersFromToken_ReturnsForbid()
    {
        _currentUser.UserType.Returns(UserType.Restaurant);
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.RestaurantId.Returns(Guid.NewGuid());

        var controller = InitController(new RestaurantCollaborativeCampaignsController(
            _currentUser, NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<RestaurantCollaborativeCampaignsController>>()));

        var request = new UpdateCapacityRequest(Guid.NewGuid(), 30);

        var result = await controller.ReduceCapacity(Guid.NewGuid(), request, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        await _sender.DidNotReceive().Send(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AdminController_NewPolicyAndRescheduleEndpoints_HaveCampaignPermissionAttributes()
    {
        var controllerType = typeof(AdminCollaborativeCampaignsController);

        var manageMethods = new[]
        {
            nameof(AdminCollaborativeCampaignsController.ApprovePricingPolicy),
            nameof(AdminCollaborativeCampaignsController.RescheduleReview),
        };

        foreach (var methodName in manageMethods)
        {
            var method = controllerType.GetMethod(methodName);
            method.Should().NotBeNull($"Method {methodName} must exist on admin controller.");
            var permAttr = method!.GetCustomAttribute<HasPermissionAttribute>();
            permAttr.Should().NotBeNull($"Method {methodName} must have [HasPermission].");
            permAttr!.Policy.Should().Contain(PermissionCodes.CampaignsManage);
        }

        var viewMethod = controllerType.GetMethod(nameof(AdminCollaborativeCampaignsController.GetPricingPolicy));
        viewMethod!.GetCustomAttribute<HasPermissionAttribute>()!.Policy.Should().Contain(PermissionCodes.CampaignsView);
    }
}
