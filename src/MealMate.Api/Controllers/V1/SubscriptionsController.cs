using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/subscriptions")]
public sealed class SubscriptionsController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? customerUserId = null,
        [FromQuery] SubscriptionStatus? status = null,
        [FromQuery] Guid? programId = null,
        [FromQuery] Guid? bundleId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetSubscriptionsListQuery(customerUserId, status, programId, bundleId, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetSubscriptionByIdQuery(id), cancellationToken));

    [HttpPost]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> Create([FromBody] CreateSubscriptionCommand command, CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(command, cancellationToken), "/api/v1/subscriptions");

    [HttpPost("{id:guid}/confirm-payment")]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> ConfirmPayment(Guid id, [FromBody] ConfirmPaymentRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ConfirmSubscriptionPaymentCommand(id, request.PaymentReference, request.CorrelationId), cancellationToken));
}

public sealed record ConfirmPaymentRequest(string PaymentReference, string? CorrelationId);
