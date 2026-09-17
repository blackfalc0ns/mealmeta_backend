using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Offers.Queries;
using MealMate.Domain.Enums;
using MealMate.Domain.Offers.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.Api.Controllers.Restaurant;

[Authorize]
[Route("api/v1/restaurant/offers")]
public sealed class RestaurantOffersController : ApiControllerBase
{
    private ICurrentUser? _currentUser;

    public RestaurantOffersController() { }

    public RestaurantOffersController(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    private ICurrentUser CurrentUser => _currentUser ??= HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    [HttpGet]
    public async Task<IActionResult> ListActiveOffers([FromQuery] PageRequest? page, CancellationToken cancellationToken)
    {
        if (CurrentUser.UserType != UserType.Restaurant || !CurrentUser.RestaurantId.HasValue)
        {
            return Forbid();
        }

        var result = await Sender.Send(new GetOffersListQuery(OfferStatus.Active, page), cancellationToken);
        return HandleResult(result);
    }
}
