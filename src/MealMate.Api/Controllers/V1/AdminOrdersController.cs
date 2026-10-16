using MealMate.Api.Common;
using MealMate.Application.Features.AdminOrders;
using MealMate.Application.Features.RestaurantOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Authorize(Policy = "AdminPolicy")]
[Route("api/v1/admin/orders")]
[Route("api/v1/admin/operations/orders")]
public sealed class AdminOrdersController : ApiControllerBase
{
    [HttpGet("{orderId:guid}/prep-documents")]
    public async Task<IActionResult> GetPrepDocuments(Guid orderId, CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetAdminOrderPrepDocumentsQuery(orderId), cancellationToken));

    [HttpGet("{orderId:guid}/invoice")]
    public async Task<IActionResult> GetInvoice(Guid orderId, CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetAdminOrderInvoiceQuery(orderId), cancellationToken));

    [HttpGet("{orderId:guid}/labels")]
    public async Task<IActionResult> GetLabels(Guid orderId, CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetAdminOrderLabelsQuery(orderId), cancellationToken));

    [HttpGet("confirmation-overdue")]
    public async Task<IActionResult> GetConfirmationOverdue(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return HandleResult(await Sender.Send(
            new GetConfirmationOverdueOrdersQuery(pageNumber, pageSize),
            cancellationToken));
    }

    [HttpPost("{orderId:guid}/manual-reassign")]
    public async Task<IActionResult> ManualReassign(
        Guid orderId,
        [FromBody] ManualReassignOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new ManualReassignOrderCommand(orderId, request.TargetRestaurantId, request.Reason);
        return HandleResult(await Sender.Send(command, cancellationToken));
    }

    [HttpPost("{orderId:guid}/open-replacement-window")]
    public async Task<IActionResult> OpenReplacementWindow(
        Guid orderId,
        [FromBody] OpenReplacementWindowOrderRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var command = new OpenReplacementWindowCommand(orderId, request?.Reason, request?.NotifyCustomer ?? true);
        return HandleResult(await Sender.Send(command, cancellationToken));
    }

    [HttpPost("{orderId:guid}/exception")]
    public async Task<IActionResult> ApplyException(
        Guid orderId,
        [FromBody] ApplyOrderExceptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new ApplyOrderExceptionCommand(
            orderId,
            request.ExceptionType,
            request.Reason,
            request.NewRestaurantId,
            request.NewMealId,
            request.ActorNote);
        return HandleResult(await Sender.Send(command, cancellationToken));
    }

    [HttpGet("replacement-windows")]
    public async Task<IActionResult> GetReplacementWindows(CancellationToken cancellationToken = default)
    {
        return HandleResult(await Sender.Send(new GetReplacementWindowsQuery(), cancellationToken));
    }
}
