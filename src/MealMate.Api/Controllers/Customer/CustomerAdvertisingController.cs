using MealMate.Api.Common;
using MealMate.Application.Features.Advertising.Customer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.Customer;

/// <summary>Customer-facing advertising surface: eligible paid placements and idempotent analytics events.</summary>
[ApiController]
[Route("api/v1/customer/advertising")]
public sealed class CustomerAdvertisingController : ApiControllerBase
{
    [AllowAnonymous]
    [HttpGet("placements")]
    public async Task<IActionResult> GetPlacements([FromQuery] string placementKey, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetEligibleAdPlacementsQuery(placementKey), cancellationToken));

    [AllowAnonymous]
    [HttpPost("events")]
    public async Task<IActionResult> RecordEvent([FromBody] RecordAdEventCommand command, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(command, cancellationToken));
}
