using MealMate.Api.Common;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[ApiController]
[Route("api/v1/driver/reassignment-requests")]
[Authorize(Policy = "DriverPolicy")]
public sealed class DriverReassignmentRequestsController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? boxId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => HandleResult(await Sender.Send(new GetDriverReassignmentRequestsQuery(boxId, page, pageSize), ct));

    [HttpGet("{requestId:guid}")]
    public async Task<IActionResult> Get(Guid requestId, CancellationToken ct)
        => HandleResult(await Sender.Send(new GetDriverReassignmentRequestQuery(requestId), ct));
}

[ApiController]
[Route("api/v1/dispatcher/reassignment-requests")]
[Authorize(Policy = "DispatcherPolicy")]
public sealed class DispatcherReassignmentRequestsController : ApiControllerBase
{
    [HttpGet("{requestId:guid}")]
    public async Task<IActionResult> Get(Guid requestId, CancellationToken ct)
        => HandleResult(await Sender.Send(new GetDriverReassignmentRequestQuery(requestId), ct));

    [HttpPost("{requestId:guid}/accept")]
    public async Task<IActionResult> Accept(Guid requestId, [FromBody] ReassignmentDecisionRequest request, CancellationToken ct)
        => HandleResult(await Sender.Send(new DecideDriverReassignmentCommand(requestId, "Accepted", request.Notes), ct));

    [HttpPost("{requestId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid requestId, [FromBody] ReassignmentDecisionRequest request, CancellationToken ct)
        => HandleResult(await Sender.Send(new DecideDriverReassignmentCommand(requestId, "Rejected", request.Notes), ct));
}

public sealed record ReassignmentDecisionRequest(string? Notes = null);
