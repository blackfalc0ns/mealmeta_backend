using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Features.RestaurantOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurants/orders")]
[Route("api/restaurant/orders")]
[AllowAnonymous]
public sealed class RestaurantOrdersController : ApiControllerBase
{
    [HttpGet("pending-confirmation")]
    public async Task<IActionResult> GetPendingConfirmation(
        [FromQuery] string? filter = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetRestaurantPendingConfirmationQuery(filter, search), cancellationToken));

    [HttpGet("detail/{orderCodeOrId}")]
    [HttpGet("{orderCodeOrId}")]
    public async Task<IActionResult> GetOrderDetail(
        string orderCodeOrId,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetRestaurantOrderDetailQuery(orderCodeOrId), cancellationToken));

    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> ConfirmOrder(
        Guid id,
        [FromBody] ConfirmRestaurantOrderRequest? request = null,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new ConfirmRestaurantOrderCommand(id, request?.Note), cancellationToken));

    [HttpPost("{id:guid}/reject-confirmation")]
    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> RejectOrder(
        Guid id,
        [FromBody] RejectRestaurantOrderRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new RejectRestaurantOrderCommand(id, request.Reason, request.ReasonCode), cancellationToken));

    [HttpGet("daily")]
    public async Task<IActionResult> GetDailyOrders(
        [FromQuery] DateOnly? date = null,
        [FromQuery] string? shift = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetRestaurantDailyOrdersQuery(date, shift, status, search), cancellationToken));

    [HttpGet("upcoming-24h")]
    public async Task<IActionResult> GetUpcoming24h(
        [FromQuery] DateOnly? targetDate = null,
        [FromQuery] string? shift = null,
        [FromQuery] string? prepStatus = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetRestaurantUpcoming24hQuery(targetDate, shift, prepStatus, search), cancellationToken));

    [HttpGet("{id:guid}/prep-documents")]
    public async Task<IActionResult> GetPrepDocuments(
        Guid id,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetRestaurantPrepDocumentsQuery(id), cancellationToken));

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> ArchiveOrder(
        Guid id,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new ArchiveDailyOrderCommand(id), cancellationToken));

    [HttpPost("dispatch-due")]
    public async Task<IActionResult> DispatchDueOrders(
        [FromBody] DispatchOrdersRequest? request = null,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new DispatchRestaurantDailyOrdersCommand(request?.RestaurantId, request?.TargetDate, request?.AutoSelectIfEmpty ?? true), cancellationToken));

    [HttpPut("daily/{dayId:guid}/prep-status")]
    public async Task<IActionResult> UpdateDailyOrderPrepStatus(
        Guid dayId,
        [FromBody] UpdateDailyOrderPrepStatusRequest request,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new UpdateDailyOrderPrepStatusCommand(dayId, request.Status), cancellationToken));
}
