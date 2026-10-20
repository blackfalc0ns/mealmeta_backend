using MealMate.Api.Common;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[ApiController]
[Route("api/v1/driver/support-handovers")]
[Authorize(Policy = "DriverPolicy")]
public sealed class DriverSupportHandoversController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetMine([FromQuery] string? status = "active", CancellationToken ct = default)
        => HandleResult(await Sender.Send(new GetMyDriverSupportHandoversQuery(status), ct));

    [HttpGet("{handoverId:guid}")]
    public async Task<IActionResult> Get(Guid handoverId, CancellationToken ct)
        => HandleResult(await Sender.Send(new GetDriverSupportHandoverQuery(handoverId), ct));

    [HttpPost("{handoverId:guid}/arrival")]
    public async Task<IActionResult> Arrive(Guid handoverId, [FromBody] DriverSupportArrivalRequest request, CancellationToken ct)
        => HandleResult(await Sender.Send(new ArriveDriverSupportHandoverCommand(handoverId, request.Latitude, request.Longitude), ct));

    [HttpPost("{handoverId:guid}/boxes/scan")]
    public async Task<IActionResult> Scan(Guid handoverId, [FromBody] DriverSupportBoxReceiptRequest request, CancellationToken ct)
        => HandleResult(await Sender.Send(new ScanDriverSupportHandoverBoxCommand(handoverId, request.Code, request.Status, request.FollowUpNote, request.Method), ct));

    [HttpPost("{handoverId:guid}/complete")]
    public async Task<IActionResult> Complete(Guid handoverId, CancellationToken ct)
        => HandleResult(await Sender.Send(new CompleteDriverSupportHandoverCommand(handoverId), ct));

    [HttpPost("{handoverId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid handoverId, [FromBody] DriverSupportCancelRequest request, CancellationToken ct)
        => HandleResult(await Sender.Send(new CancelDriverSupportHandoverCommand(handoverId, request.Reason), ct));
}

public sealed record DriverSupportArrivalRequest(decimal Latitude, decimal Longitude);
public sealed record DriverSupportBoxReceiptRequest(string Code, string Status = "Received", string Method = "Barcode", string? FollowUpNote = null);
public sealed record DriverSupportCancelRequest(string? Reason = null);
