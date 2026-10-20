using FluentAssertions;
using MealMate.Api.Controllers.Customer;
using MealMate.Api.Localization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
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

public class CustomerCampaignDiscoveryControllerTests
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

    private CustomerCollaborativeCampaignsController InitController()
    {
        var controller = new CustomerCollaborativeCampaignsController();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { RequestServices = CreateServices() }
        };
        return controller;
    }

    [Fact]
    public async Task GetCampaigns_WhenNotCustomer_ReturnsForbid()
    {
        _currentUser.UserType.Returns(UserType.Restaurant);
        _currentUser.UserId.Returns(Guid.NewGuid());
        var controller = InitController();

        var response = await controller.GetCampaigns(20, null, CancellationToken.None);

        response.Should().BeOfType<ForbidResult>();
        await _sender.DidNotReceive().Send(Arg.Any<GetCustomerCampaignsQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCampaigns_WhenCustomer_SendsQueryWithAuthenticatedId_ReturnsPage()
    {
        var customerId = Guid.NewGuid();
        _currentUser.UserType.Returns(UserType.Customer);
        _currentUser.UserId.Returns(customerId);

        var page = new CustomerCampaignsPageDto(Array.Empty<CustomerCampaignListItemDto>(), null, DateTime.UtcNow);
        GetCustomerCampaignsQuery? received = null;
        _sender.Send(Arg.Do<GetCustomerCampaignsQuery>(q => received = q), Arg.Any<CancellationToken>())
            .Returns(Result.Success(page));

        var controller = InitController();
        var response = await controller.GetCampaigns(20, null, CancellationToken.None);

        var ok = response.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(page);
        received.Should().NotBeNull();
        received!.CustomerId.Should().Be(customerId);
        received.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task GetCampaignDetails_WhenNotCustomer_ReturnsForbid()
    {
        _currentUser.UserType.Returns(UserType.Driver);
        _currentUser.UserId.Returns(Guid.NewGuid());
        var controller = InitController();

        var response = await controller.GetCampaignDetails(Guid.NewGuid(), null, null, CancellationToken.None);

        response.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetCampaignDetails_WhenCustomer_SendsQueryWithAuthenticatedIdAndOptionFilters()
    {
        var customerId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        _currentUser.UserType.Returns(UserType.Customer);
        _currentUser.UserId.Returns(customerId);

        var details = new CustomerCampaignDetailsDto(
            campaignId, "حملة", "Campaign",
            null, null,
            DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddDays(10), DateTime.UtcNow,
            true, 5,
            Array.Empty<CustomerCampaignBlock>(),
            Array.Empty<CustomerCampaignOptionDto>());
        GetCustomerCampaignDetailsQuery? received = null;
        _sender.Send(Arg.Do<GetCustomerCampaignDetailsQuery>(q => received = q), Arg.Any<CancellationToken>())
            .Returns(Result.Success(details));

        var controller = InitController();
        var response = await controller.GetCampaignDetails(campaignId, bundleId, 26, CancellationToken.None);

        var ok = response.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(details);
        received!.CustomerId.Should().Be(customerId);
        received.CampaignId.Should().Be(campaignId);
        received.BundleId.Should().Be(bundleId);
        received.DurationDays.Should().Be(26);
    }

    [Fact]
    public async Task GetCampaignDetails_WhenGone_PreservesProblemStatus()
    {
        var customerId = Guid.NewGuid();
        _currentUser.UserType.Returns(UserType.Customer);
        _currentUser.UserId.Returns(customerId);
        _sender.Send(Arg.Any<GetCustomerCampaignDetailsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<CustomerCampaignDetailsDto>(
                Error.Gone("Campaign.NoLongerAvailable", "This campaign is no longer available.")));

        var controller = InitController();
        var response = await controller.GetCampaignDetails(Guid.NewGuid(), null, null, CancellationToken.None);

        var objectResult = response.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status410Gone);
    }
}
