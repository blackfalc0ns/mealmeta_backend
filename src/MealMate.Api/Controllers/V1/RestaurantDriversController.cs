using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.DriverRegistration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurants/drivers")]
[Authorize]
public sealed class RestaurantDriversController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? restaurantId = null,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var targetRestaurantId = restaurantId ?? Guid.Empty;
        var result = await Sender.Send(new ListRestaurantDriversQuery(targetRestaurantId, isActive), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRestaurantDriverCommand body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(body, cancellationToken);
        return HandleCreated(result, "/api/v1/restaurants/drivers");
    }
}
