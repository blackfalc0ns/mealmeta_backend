using System.Reflection;
using FluentAssertions;
using MealMate.Api.Controllers.Customer;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.Offers.DTOs;
using MealMate.Application.Features.Offers.Queries;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.Controllers;

public class OfferPurchaseOptionsControllerTests
{
    [Fact]
    public void GetOfferPurchaseOptions_HasAuthorizeAndCorrectRoute()
    {
        var method = typeof(CustomerOffersController).GetMethod(nameof(CustomerOffersController.GetOfferPurchaseOptions));
        method.Should().NotBeNull();

        var authAttr = method!.GetCustomAttribute<AuthorizeAttribute>();
        authAttr.Should().NotBeNull();

        var httpGet = method.GetCustomAttribute<HttpGetAttribute>();
        httpGet.Should().NotBeNull();
        httpGet!.Template.Should().Be("{offerId:guid}/purchase-options");
    }

    [Fact]
    public async Task GetOfferPurchaseOptions_ReturnsForbid_WhenUserIsNotCustomer()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserType.Returns(UserType.Driver);
        currentUser.UserId.Returns(Guid.NewGuid());

        var controller = new CustomerOffersController(currentUser);

        var result = await controller.GetOfferPurchaseOptions(Guid.NewGuid(), null, false, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetOfferPurchaseOptions_ReturnsForbid_WhenCustomerUserIdIsNull()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserType.Returns(UserType.Customer);
        currentUser.UserId.Returns((Guid?)null);

        var controller = new CustomerOffersController(currentUser);

        var result = await controller.GetOfferPurchaseOptions(Guid.NewGuid(), null, false, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetOfferPurchaseOptions_DispatchesQuery_WithAuthenticatedCustomerAndParameters()
    {
        var customerId = Guid.NewGuid();
        var offerId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 11, 1);
        var includeFriday = true;

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserType.Returns(UserType.Customer);
        currentUser.UserId.Returns(customerId);

        var sender = Substitute.For<ISender>();
        var response = new CustomerOfferPurchaseOptionsResponseDto(
            offerId,
            "Individual",
            DateTime.UtcNow,
            new CustomerSubscriptionPrerequisitesDto(true, true, true, true, Guid.NewGuid()),
            Array.Empty<MobileBlockingReasonDto>(),
            Array.Empty<OfferPurchaseOptionDto>()
        );

        sender.Send(Arg.Is<GetCustomerOfferPurchaseOptionsQuery>(q =>
            q.OfferId == offerId
            && q.CustomerId == customerId
            && q.StartDate == startDate
            && q.IncludeFriday == includeFriday), Arg.Any<CancellationToken>())
            .Returns(Result.Success(response));

        var controller = new CustomerOffersController(currentUser);
        var httpContext = new DefaultHttpContext();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(ISender)).Returns(sender);
        httpContext.RequestServices = serviceProvider;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.GetOfferPurchaseOptions(offerId, startDate, includeFriday, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        okResult.Value.Should().Be(response);
    }

    [Fact]
    public async Task GetOfferPurchaseOptions_ReturnsNotFound_WhenOfferInaccessible()
    {
        var customerId = Guid.NewGuid();
        var offerId = Guid.NewGuid();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserType.Returns(UserType.Customer);
        currentUser.UserId.Returns(customerId);

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetCustomerOfferPurchaseOptionsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<CustomerOfferPurchaseOptionsResponseDto>(
                Error.NotFound("offers.customer.unavailable", string.Empty)));

        var controller = new CustomerOffersController(currentUser);
        var httpContext = new DefaultHttpContext();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(ISender)).Returns(sender);
        httpContext.RequestServices = serviceProvider;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.GetOfferPurchaseOptions(offerId, null, false, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be(404);
    }
}
