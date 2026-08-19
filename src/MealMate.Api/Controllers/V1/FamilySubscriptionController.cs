using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.ProgramsSubscriptions;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

/// <summary>
/// Legacy family-member endpoints. Prefer <c>/api/v1/customer/family-subscription-flow</c> for the current multi-member checkout.
/// </summary>
[Route("api/v1/family_subscription")]
public sealed class FamilySubscriptionController : ApiControllerBase
{
    [HttpGet("{primarySubscriptionId:guid}/members")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> GetMembers(Guid primarySubscriptionId, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetFamilyMembersQuery(primarySubscriptionId), cancellationToken));

    [HttpPost("members")]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> AddMember([FromBody] AddFamilyMemberCommand command, CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(command, cancellationToken), $"/api/v1/family_subscription/{command.PrimarySubscriptionId}/members");

    [HttpDelete("members/{memberId:guid}")]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> RemoveMember(Guid memberId, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new RemoveFamilyMemberCommand(memberId), cancellationToken));
}
