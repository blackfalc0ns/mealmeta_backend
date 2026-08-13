using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurant_offerings")]
public sealed class RestaurantOfferingsController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;
    public RestaurantOfferingsController(ICurrentUser currentUser) => _currentUser = currentUser;

    [HttpGet]
    [HasPermission(PermissionCodes.BundlesView)]
    public async Task<IActionResult> Get([FromQuery] Guid restaurantId, CancellationToken cancellationToken)
        => CanAccess(restaurantId) ? HandleResult(await Sender.Send(new GetRestaurantOfferingsQuery(restaurantId), cancellationToken)) : Forbid();

    [HttpPut]
    [HasPermission(PermissionCodes.BundlesManage)]
    public async Task<IActionResult> Save([FromBody] SaveRestaurantOfferingsRequest request, CancellationToken cancellationToken)
        => CanAccess(request.RestaurantId) ? HandleResult(await Sender.Send(new SaveRestaurantOfferingsCommand(request.RestaurantId, request.Offerings), cancellationToken)) : Forbid();

    private bool CanAccess(Guid restaurantId) => _currentUser.UserType == UserType.Admin || _currentUser.RestaurantId == restaurantId;
}

public sealed record SaveRestaurantOfferingsRequest(Guid RestaurantId, IReadOnlyList<RestaurantOfferingDto> Offerings);
