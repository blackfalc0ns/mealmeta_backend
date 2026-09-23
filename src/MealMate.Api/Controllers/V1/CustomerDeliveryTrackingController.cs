using MealMate.Api.Common;
using MealMate.Application.Features.DriverDelivery.CustomerTracking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[ApiController]
[Route("api/v1/customer/orders")]
[Authorize]
public sealed class CustomerDeliveryTrackingController : ApiControllerBase
{
    /// <summary>
    /// Returns live tracking details for an active customer meal delivery order.
    /// Includes driver location, live ETA, vehicle plate, masked phone, and delivery status.
    /// </summary>
    [HttpGet("{orderId:guid}/live-tracking")]
    public async Task<IActionResult> GetLiveTracking(
        [FromRoute] Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetCustomerLiveOrderTrackingQuery(orderId), cancellationToken);
        return HandleResult(result);
    }
}
