using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurants/delivery-managers")]
[Authorize]
public sealed class RestaurantDeliveryManagersController : ApiControllerBase
{
    [HttpPost]
    [HasPermission(PermissionCodes.DeliveryManagersManage)]
    public async Task<IActionResult> Create(
        [FromBody] CreateRestaurantDeliveryManagerRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new RegisterDeliveryManagerCommand(
            body.FullName,
            body.Email,
            body.Phone,
            body.Password,
            body.RestaurantId), cancellationToken);

        return HandleCreated(result, "/api/v1/auth/delivery-managers/login");
    }
}

public sealed record CreateRestaurantDeliveryManagerRequest(
    string FullName,
    string Email,
    string Phone,
    string Password,
    Guid? RestaurantId = null);
