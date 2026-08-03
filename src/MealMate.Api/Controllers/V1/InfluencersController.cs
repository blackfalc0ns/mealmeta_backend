using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Influencer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/influencers")]
public sealed class InfluencersController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.InfluencerView)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ListInfluencersQuery(), cancellationToken));

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterInfluencerCommand body, CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(body, cancellationToken), "/api/v1/influencers");

    [HttpPost("promote-customer")]
    [HasPermission(PermissionCodes.InfluencerRegCreate)]
    public async Task<IActionResult> PromoteCustomer(
        [FromBody] PromoteCustomerToInfluencerRequest body,
        CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(new PromoteCustomerToInfluencerCommand(
            body.CustomerUserId,
            body.DisplayName,
            body.InfluencerCode,
            body.DefaultCommissionRate,
            body.ActivateNow), cancellationToken), "/api/v1/influencers");

    [HttpPost("{id:guid}/link")]
    [HasPermission(PermissionCodes.InfluencerLink)]
    public async Task<IActionResult> Link(Guid id, [FromBody] LinkRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new LinkInfluencerCustomerCommand(id, body.CustomerUserId, body.Reason), cancellationToken));

    [HttpPost("{id:guid}/unlink")]
    [HasPermission(PermissionCodes.InfluencerLink)]
    public async Task<IActionResult> Unlink(Guid id, [FromBody] DecisionRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UnlinkInfluencerCustomerCommand(id, body.Reason), cancellationToken));
}

public sealed record PromoteCustomerToInfluencerRequest(
    Guid CustomerUserId,
    string DisplayName,
    string InfluencerCode,
    decimal DefaultCommissionRate,
    bool ActivateNow = true);

public sealed record LinkRequest(Guid CustomerUserId, string Reason);
