using MealMate.Api.Controllers.Restaurant;
using MealMate.Api.Controllers.Customer;
using MealMate.Api.Resources;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Advertising.Restaurant;
using MealMate.Application.Features.Offers.DTOs;
using MealMate.Application.Features.Offers.Queries;
using MealMate.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MealMate.ApiTests.Controllers;

public sealed class AdvertisingMobileBoundaryTests
{
    [Fact]
    public async Task RestaurantBidPage_ForwardsPagination_AndOfferResponseForwardsConsent()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetMyAdBidPageQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new MyAdBidPageDto([], 0, 1, 20, 0, 0, [])));
        sender.Send(Arg.Any<RespondToAdAuctionNextBidderOfferCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        using var services = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        var controller = new RestaurantAdvertisingController { ControllerContext = new ControllerContext {
            HttpContext = new DefaultHttpContext { RequestServices = services } } };
        Assert.IsType<OkObjectResult>(await controller.GetMyBidPage(CancellationToken.None, 3, 40));
        await sender.Received(1).Send(Arg.Is<GetMyAdBidPageQuery>(q => q.Page == 3 && q.PageSize == 40), Arg.Any<CancellationToken>());
        var offerId = Guid.NewGuid();
        Assert.IsType<NoContentResult>(await controller.RespondToNextBidderOffer(offerId, new(false, "Declined by owner"), CancellationToken.None));
        await sender.Received(1).Send(Arg.Is<RespondToAdAuctionNextBidderOfferCommand>(q =>
            q.OfferId == offerId && !q.Accept && q.Reason == "Declined by owner"), Arg.Any<CancellationToken>());
        var permissions = typeof(RestaurantAdvertisingController).GetMethod(nameof(RestaurantAdvertisingController.RespondToNextBidderOffer))!
            .GetCustomAttributes(typeof(MealMate.Api.Authorization.HasPermissionAttribute), false);
        Assert.Contains(permissions, p => ((MealMate.Api.Authorization.HasPermissionAttribute)p).Policy!.EndsWith(MealMate.Application.Common.Security.PermissionCodes.CampaignsManage));
    }

    [Theory]
    [InlineData("Advertising.SpaceInactive")]
    [InlineData("Advertising.ReasonTooLong")]
    public void RestaurantAdvertisingErrors_HaveArabicAndEnglishResources(string code)
    {
        var manager = new System.Resources.ResourceManager("MealMate.Api.Resources.Resources.Messages", typeof(Messages).Assembly);
        var ar = manager.GetString(code, System.Globalization.CultureInfo.GetCultureInfo("ar"));
        var en = manager.GetString(code, System.Globalization.CultureInfo.GetCultureInfo("en"));
        Assert.False(string.IsNullOrWhiteSpace(ar));
        Assert.False(string.IsNullOrWhiteSpace(en));
        Assert.NotEqual(ar, en);
    }
    [Fact]
    public async Task LegacyOfferDetails_UsesCustomerVisibilityAndKeepsFlatResponse()
    {
        var customerId = Guid.NewGuid();
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserType.Returns(UserType.Customer);
        user.UserId.Returns(customerId);
        var sender = Substitute.For<ISender>();
        // A draft/ineligible offer is never sent through the unrestricted administrative query.
        sender.Send(Arg.Any<GetCustomerOfferDetailsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<CustomerOfferDetailsDto>(Error.NotFound("offers.customer.unavailable", string.Empty)));
        using var services = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        var controller = new CustomerOffersController(user)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services }
            }
        };
        var id = Guid.NewGuid();

        var result = await controller.GetById(id, CancellationToken.None);

        Assert.Equal(404, Assert.IsType<ObjectResult>(result).StatusCode);
        await sender.Received(1).Send(Arg.Is<GetCustomerOfferDetailsQuery>(q =>
            q.OfferId == id && q.CustomerId == customerId), Arg.Any<CancellationToken>());
        await sender.DidNotReceive().Send(Arg.Any<GetOfferByIdQuery>(), Arg.Any<CancellationToken>());

        var offer = MealMate.Application.Features.Offers.Commands.CreateOfferDraftCommandHandler.MapOfferDto(
            new MealMate.Domain.Offers.Offer { NameAr = "عرض", NameEn = "Offer" });
        sender.Send(Arg.Any<GetCustomerOfferDetailsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new CustomerOfferDetailsDto(offer, DateTime.UtcNow)));
        var visible = await controller.GetById(id, CancellationToken.None);
        Assert.IsType<OfferDto>(Assert.IsType<OkObjectResult>(visible).Value);
    }

    [Fact]
    public void UnavailableOfferMessage_ResolvesInBothLanguagesAndSwitchesBack()
    {
        var manager = new System.Resources.ResourceManager("MealMate.Api.Resources.Resources.Messages", typeof(Messages).Assembly);
        var arabic = manager.GetString("offers.customer.unavailable", System.Globalization.CultureInfo.GetCultureInfo("ar"));
        var english = manager.GetString("offers.customer.unavailable", System.Globalization.CultureInfo.GetCultureInfo("en"));
        Assert.Equal("هذا العرض غير متاح أو لا ينطبق على اختياراتك.", arabic);
        Assert.Equal("This offer is unavailable or does not apply to your selection.", english);
        Assert.Equal(arabic, manager.GetString("offers.customer.unavailable", System.Globalization.CultureInfo.GetCultureInfo("ar")));
    }

    [Fact]
    public async Task SubmitCreative_UsesRouteAuctionIdInsteadOfBody()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<SubmitAdCreativeCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        using var services = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        var controller = new RestaurantAdvertisingController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services }
            }
        };
        var routeId = Guid.NewGuid();
        var request = new SubmitAdCreativeCommand(Guid.NewGuid(), "owned-asset", "Restaurant", Guid.NewGuid(),
            DateTime.UtcNow, DateTime.UtcNow.AddDays(7));

        var result = await controller.SubmitCreative(routeId, request, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        await sender.Received(1).Send(Arg.Is<SubmitAdCreativeCommand>(c =>
            c.AuctionId == routeId && c.CreativeStorageKey == request.CreativeStorageKey), Arg.Any<CancellationToken>());
    }
}
