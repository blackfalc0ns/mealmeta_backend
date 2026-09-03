using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/subscription_lifecycle_operations")]
public sealed class SubscriptionLifecycleOperationsController : ApiControllerBase
{
    [HttpGet("freeze-requests")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> GetFreezeRequests([FromQuery] VerificationStatus? status, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetSubscriptionFreezeRequestsQuery(status), cancellationToken));

    [HttpGet("upgrade-requests")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> GetUpgradeRequests([FromQuery] VerificationStatus? status, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetSubscriptionUpgradeRequestsQuery(status), cancellationToken));

    [HttpGet("cancellation-requests")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> GetCancellationRequests(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetSubscriptionCancellationRequestsQuery(), cancellationToken));

    [HttpPost("freeze")]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> RequestFreeze([FromBody] RequestSubscriptionFreezeCommand command, CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(command, cancellationToken), "/api/v1/subscription_lifecycle_operations/freeze");

    [HttpPost("freeze/{id:guid}/approve")]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> ApproveFreeze(Guid id, [FromBody] ApproveFreezeRequest? request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ApproveSubscriptionFreezeCommand(id, request?.Approved ?? true), cancellationToken));

    [HttpPost("upgrade")]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> RequestUpgrade([FromBody] RequestSubscriptionUpgradeCommand command, CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(command, cancellationToken), "/api/v1/subscription_lifecycle_operations/upgrade");

    [HttpPost("upgrade/{id:guid}/approve")]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> ApproveUpgrade(Guid id, [FromBody] ApproveUpgradeRequest? request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ApproveSubscriptionUpgradeCommand(id, request?.Approved ?? true), cancellationToken));

    [HttpPost("process-expirations")]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> ProcessExpirations(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ProcessSubscriptionRenewalsAndExpirationsCommand(), cancellationToken));
}

public sealed record ApproveFreezeRequest(bool Approved = true);
public sealed record ApproveUpgradeRequest(bool Approved = true);
