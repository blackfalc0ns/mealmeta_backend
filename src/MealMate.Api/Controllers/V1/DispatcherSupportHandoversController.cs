using MealMate.Api.Common;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[ApiController]
[Route("api/v1/dispatcher/support-handovers")]
[Authorize(Policy = "DispatcherPolicy")]
public sealed class DispatcherSupportHandoversController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] Guid? restaurantId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => HandleResult(await Sender.Send(new GetDispatcherSupportHandoversQuery(status, restaurantId, page, pageSize), ct));

    [HttpGet("{handoverId:guid}")]
    public async Task<IActionResult> Get(Guid handoverId, CancellationToken ct)
        => HandleResult(await Sender.Send(new GetDriverSupportHandoverQuery(handoverId), ct));

    [HttpPost("issues/{issueId:guid}/assign")]
    public async Task<IActionResult> Assign(Guid issueId, [FromBody] DriverSupportAssignRequest request, CancellationToken ct)
        => HandleResult(await Sender.Send(new CreateDriverSupportHandoverCommand(issueId, request.ReplacementDriverId, request.IdempotencyKey, request.Notes), ct));

    [HttpPost("{handoverId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid handoverId, [FromBody] DriverSupportCancelRequest request, CancellationToken ct)
        => HandleResult(await Sender.Send(new CancelDriverSupportHandoverCommand(handoverId, request.Reason), ct));

    [HttpPost("{handoverId:guid}/boxes/{tripStopId:guid}/follow-up/return-to-kitchen")]
    public async Task<IActionResult> ReturnFollowUpToKitchen(Guid handoverId, Guid tripStopId, [FromBody] DriverSupportFollowUpResolutionRequest request, CancellationToken ct)
        => HandleResult(await Sender.Send(new ResolveDriverSupportHandoverFollowUpCommand(handoverId, tripStopId, request.ManifestCode, request.ResolutionNote), ct));
}

public sealed record DriverSupportAssignRequest(Guid ReplacementDriverId, string IdempotencyKey, string? Notes = null);
public sealed record DriverSupportFollowUpResolutionRequest(string ManifestCode, string ResolutionNote);
