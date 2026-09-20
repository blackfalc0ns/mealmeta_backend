using FluentAssertions;
using MealMate.Api.Controllers.Admin;
using MealMate.Api.Controllers.Customer;
using MealMate.Api.Controllers.Restaurant;
using MealMate.Api.Controllers.Webhooks;
using MealMate.Api.Localization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
using MealMate.Application.Features.CollaborativeCampaigns.Queries;
using MealMate.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.Controllers;

public class CollaborativeCampaignsControllerTests
{
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Microsoft.Extensions.Configuration.IConfiguration _config = Substitute.For<Microsoft.Extensions.Configuration.IConfiguration>();

    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_sender);
        services.AddSingleton(_currentUser);
        services.AddSingleton(_config);
        services.AddSingleton(new ProblemDetailsLocalizer(Substitute.For<ILocalizedMessageProvider>()));
        return services.BuildServiceProvider();
    }

    private TController InitController<TController>(TController controller) where TController : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { RequestServices = CreateServices() }
        };
        return controller;
    }

    [Fact]
    public async Task Admin_CreateCampaign_WhenSuccessful_ReturnsCreated()
    {
        // Arrange
        var controller = InitController(new AdminCollaborativeCampaignsController());
        var command = new CreateCampaignDraftCommand(
            "حملة الخريف",
            "Autumn Campaign",
            DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow.AddDays(10),
            0.20m,
            0.10m,
            0.10m,
            CampaignFundingMode.Shared,
            100);

        var campaignId = Guid.NewGuid();
        var draftDto = new CampaignDraftDto(
            campaignId,
            command.NameAr,
            command.NameEn,
            CampaignStatus.Draft,
            command.StartsAtUtc,
            command.EndsAtUtc,
            0.20m,
            0.10m,
            0.10m,
            CampaignFundingMode.Shared,
            100,
            null,
            null,
            Array.Empty<Guid>(),
            Array.Empty<Guid>());

        _sender.Send(command, Arg.Any<CancellationToken>())
            .Returns(Result.Success(draftDto));

        // Act
        var response = await controller.Create(command, CancellationToken.None);

        // Assert
        var created = response.Should().BeOfType<CreatedResult>().Subject;
        created.Value.Should().Be(draftDto);
    }

    [Fact]
    public async Task Admin_TransitionStatus_WhenInvalidTransition_ReturnsConflictProblemDetails()
    {
        // Arrange
        var controller = InitController(new AdminCollaborativeCampaignsController());
        var campaignId = Guid.NewGuid();
        var command = new TransitionCampaignStatusCommand(campaignId, CampaignStatus.Active, "Direct activation");

        _sender.Send(command, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.Conflict("Campaign.InvalidStateTransition", "Transition from Draft to Active is not allowed.")));

        // Act
        var response = await controller.Transition(campaignId, command, CancellationToken.None);

        // Assert
        var objectResult = response.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Admin_StopCampaign_WhenSuccessful_ReturnsOk()
    {
        // Arrange
        var controller = InitController(new AdminCollaborativeCampaignsController());
        var campaignId = Guid.NewGuid();
        var command = new StopCampaignCommand(campaignId, "Emergency maintenance", "admin_1");

        _sender.Send(command, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new CampaignStatusChangeResultDto(campaignId, "Active", "Stopped", "Emergency maintenance")));

        // Act
        var response = await controller.Stop(campaignId, command, CancellationToken.None);

        // Assert
        var okResult = response.Should().BeOfType<OkObjectResult>().Subject;
        var result = okResult.Value.Should().BeOfType<CampaignStatusChangeResultDto>().Subject;
        result.NewStatus.Should().Be("Stopped");
    }

    [Fact]
    public async Task Admin_ReduceCapacity_WhenBelowReserved_ReturnsConflictProblemDetails()
    {
        // Arrange
        var controller = InitController(new AdminCollaborativeCampaignsController());
        var campaignId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var serviceDate = new DateOnly(2026, 10, 15);
        var command = new ReduceCampaignCapacityCommand(
            campaignId,
            restaurantId,
            serviceDate,
            10,
            "Reducing capacity");

        _sender.Send(command, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<CampaignCapacityDto>(
                Error.Conflict("Capacity.CannotReduceBelowReserved", "Requested capacity 10 is less than currently reserved boxes 15.")));

        // Act
        var response = await controller.ReduceCapacity(campaignId, command, CancellationToken.None);

        // Assert
        var objectResult = response.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Restaurant_RequestCapacityChange_WhenSuccessful_ReturnsOk()
    {
        // Arrange
        var controller = InitController(new RestaurantCollaborativeCampaignsController(_currentUser, NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<RestaurantCollaborativeCampaignsController>>()));
        var campaignId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        _currentUser.UserType.Returns(UserType.Restaurant);
        _currentUser.RestaurantId.Returns(restaurantId);

        var serviceDate = new DateOnly(2026, 10, 20);
        var command = new SubmitCapacityChangeRequestCommand(
            campaignId,
            restaurantId,
            serviceDate,
            50,
            "High kitchen load");

        var responseDto = new CapacityChangeRequestDto(
            Guid.NewGuid(),
            campaignId,
            restaurantId,
            serviceDate,
            50,
            "PendingReview",
            "High kitchen load");

        _sender.Send(command, Arg.Any<CancellationToken>())
            .Returns(Result.Success(responseDto));

        // Act
        var response = await controller.RequestCapacityChange(campaignId, command, CancellationToken.None);

        // Assert
        var okResult = response.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(responseDto);
    }

    [Fact]
    public async Task Customer_GetPricePreview_WhenValid_ReturnsPreview()
    {
        // Arrange
        var controller = InitController(new CustomerCollaborativeCampaignsController());
        var query = new GetCampaignPricePreviewQuery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            26,
            Guid.NewGuid());

        var preview = new CampaignPricePreviewDto(
            query.CampaignId,
            "حملة الخريف",
            "Autumn Campaign",
            query.BundleId,
            26,
            100.000m,
            0.20m,
            20.000m,
            0.10m,
            10.000m,
            0.10m,
            10.000m,
            80.000m,
            3.077m,
            "KWD",
            true,
            Array.Empty<string>());

        _sender.Send(query, Arg.Any<CancellationToken>())
            .Returns(Result.Success(preview));

        // Act
        var response = await controller.GetPricePreview(query, CancellationToken.None);

        // Assert
        var okResult = response.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(preview);
    }

    [Fact]
    public async Task Customer_CancelSubscription_WhenSuccessful_ReturnsOk()
    {
        // Arrange
        var controller = InitController(new CustomerCollaborativeCampaignsController());
        var subscriptionId = Guid.NewGuid();
        _currentUser.UserType.Returns(UserType.Customer);
        _currentUser.UserId.Returns(Guid.NewGuid());

        var command = new CancelCampaignSubscriptionCommand(
            subscriptionId,
            "Moving abroad",
            true);

        var resultDto = new CancelCampaignSubscriptionResultDto(
            subscriptionId,
            Guid.NewGuid(),
            "Cancelled",
            24.000m,
            "Moving abroad");

        _sender.Send(command, Arg.Any<CancellationToken>())
            .Returns(Result.Success(resultDto));

        // Act
        var response = await controller.CancelSubscription(subscriptionId, command, CancellationToken.None);

        // Assert
        var okResult = response.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(resultDto);
    }

    [Fact]
    public async Task PaymentWebhook_ProcessCallback_WhenSuccess_ReturnsOk()
    {
        // Arrange
        var controller = InitController(new CampaignPaymentWebhookController());
        var command = new ProcessPaymentCallbackCommand(
            Guid.NewGuid(),
            "MyFatoorah",
            "TXN-9999",
            null,
            80.000m,
            "KWD",
            true);

        var resultDto = new ProcessPaymentCallbackResultDto(
            command.CheckoutSessionId,
            Guid.NewGuid(),
            "Succeeded",
            "Paid",
            false);

        _sender.Send(command, Arg.Any<CancellationToken>())
            .Returns(Result.Success(resultDto));

        // Act
        var response = await controller.ProcessCallback(command, CancellationToken.None);

        // Assert
        var okResult = response.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(resultDto);
    }
}
