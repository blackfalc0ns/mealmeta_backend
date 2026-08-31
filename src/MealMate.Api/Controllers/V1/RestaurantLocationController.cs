using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurants/location")]
[Authorize]
public sealed class RestaurantLocationController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetRestaurantLocationQuery(), cancellationToken));

    [HttpPut]
    public async Task<IActionResult> Update(
        [FromBody] UpdateRestaurantLocationRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(
            new UpdateRestaurantLocationCommand(body.Latitude, body.Longitude, body.Address),
            cancellationToken));
}

[Route("api/v1/admin/restaurants/{restaurantId:guid}/location")]
[Authorize]
public sealed class AdminRestaurantLocationController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> Get(Guid restaurantId, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetRestaurantLocationQuery(restaurantId), cancellationToken));

    [HttpPut]
    [HasPermission(PermissionCodes.RestaurantRegApprove)]
    public async Task<IActionResult> Update(
        Guid restaurantId,
        [FromBody] UpdateRestaurantLocationRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(
            new UpdateRestaurantLocationCommand(body.Latitude, body.Longitude, body.Address, restaurantId),
            cancellationToken));
}

public sealed record UpdateRestaurantLocationRequest(
    decimal Latitude,
    decimal Longitude,
    string Address);
