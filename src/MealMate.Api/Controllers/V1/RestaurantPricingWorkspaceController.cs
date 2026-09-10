using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurants/pricing")]
[Authorize]
public sealed class RestaurantPricingWorkspaceController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly IRestaurantPricingWorkspaceService _workspaceService;

    public RestaurantPricingWorkspaceController(
        ICurrentUser currentUser,
        IRestaurantPricingWorkspaceService workspaceService)
    {
        _currentUser = currentUser;
        _workspaceService = workspaceService;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (_currentUser.RestaurantId is not { } restaurantId)
            return HandleResult(Error.Forbidden("auth.forbidden", "A restaurant account is required."));

        return HandleResult(await _workspaceService.GetWorkspaceAsync(restaurantId, cancellationToken));
    }

    [HttpPut]
    public async Task<IActionResult> Save([FromBody] SaveMyRestaurantPricingRequest body, CancellationToken cancellationToken)
    {
        if (_currentUser.RestaurantId is not { } restaurantId)
            return HandleResult(Error.Forbidden("auth.forbidden", "A restaurant account is required."));

        return HandleResult(await _workspaceService.SavePricingAsync(restaurantId, body, cancellationToken));
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> GetAvailableCatalog(CancellationToken cancellationToken)
    {
        if (_currentUser.RestaurantId is not { } restaurantId)
            return HandleResult(Error.Forbidden("auth.forbidden", "A restaurant account is required."));

        return HandleResult(await _workspaceService.GetAvailableCatalogAsync(restaurantId, cancellationToken));
    }

    [HttpPost("offers")]
    public async Task<IActionResult> AddOffer([FromBody] AddRestaurantPricingOfferRequest body, CancellationToken cancellationToken)
    {
        if (_currentUser.RestaurantId is not { } restaurantId)
            return HandleResult(Error.Forbidden("auth.forbidden", "A restaurant account is required."));

        return HandleResult(await _workspaceService.AddOfferAsync(restaurantId, body, cancellationToken));
    }
}
