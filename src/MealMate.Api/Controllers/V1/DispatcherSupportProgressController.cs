using MealMate.Api.Common;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[ApiController]
[Route("api/v1/dispatcher/support/issues")]
[Authorize(Policy = "DispatcherPolicy")]
public sealed class DispatcherSupportProgressController : ApiControllerBase
{
    [HttpPost("{issueId:guid}/progress")]
    public async Task<IActionResult> Update(Guid issueId, [FromBody] DispatcherSupportProgressRequest request, CancellationToken ct)
        => HandleResult(await Sender.Send(new UpdateDriverSupportProgressCommand(issueId, request.Stage, request.Notes), ct));
}
