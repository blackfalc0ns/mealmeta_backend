using MealMate.Api.Common;
using MealMate.Application.Features.DriverDelivery.CustomerTracking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

/// <summary>
/// Read-only customer delivery details for one subscription day. Independent of the
/// trip-stop-based live-tracking route: it opens before a driver is assigned and stays
/// available after delivery. Authorization is enforced inside the handler; another
/// customer's day is reported as the same not-found as a missing day.
/// </summary>
[ApiController]
[Route("api/v1/customer")]
[Authorize]
public sealed class CustomerDeliveryDetailsController : ApiControllerBase
{
    [HttpGet("subscription-days/{subscriptionDayId:guid}/delivery-details")]
    [ProducesResponseType(typeof(CustomerDeliveryDetailsResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDeliveryDetails(
        [FromRoute] Guid subscriptionDayId,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(
            new GetCustomerDeliveryDetailsQuery(subscriptionDayId), cancellationToken));
}
