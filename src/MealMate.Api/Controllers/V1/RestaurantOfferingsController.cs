using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.ProgramsSubscriptions;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurant_offerings")]
public sealed class RestaurantOfferingsController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.BundlesView)]
    public async Task<IActionResult> Get([FromQuery] Guid restaurantId, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetRestaurantOfferingsQuery(restaurantId), cancellationToken));

    [HttpPut]
    [HasPermission(PermissionCodes.BundlesManage)]
    public async Task<IActionResult> Save([FromBody] SaveRestaurantOfferingsRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SaveRestaurantOfferingsCommand(request.RestaurantId, request.Offerings), cancellationToken));
}

public sealed record SaveRestaurantOfferingsRequest(Guid RestaurantId, IReadOnlyList<RestaurantOfferingDto> Offerings);
