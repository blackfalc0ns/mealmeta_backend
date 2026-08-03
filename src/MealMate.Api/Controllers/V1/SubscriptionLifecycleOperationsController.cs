using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.ProgramsSubscriptions;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/subscription_lifecycle_operations")]
public sealed class SubscriptionLifecycleOperationsController : ApiControllerBase
{
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
