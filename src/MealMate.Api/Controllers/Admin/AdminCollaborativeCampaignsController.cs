using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.Admin;

[Authorize]
[Route("api/v1/admin/campaigns")]
public sealed class AdminCollaborativeCampaignsController : ApiControllerBase
{
    [HttpPost]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> Create([FromBody] CreateCampaignDraftCommand command, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        return HandleCreated(result, $"api/v1/admin/campaigns/{result.Value?.Id}");
    }

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> UpdateDraft(Guid id, [FromBody] UpdateCampaignDraftCommand command, CancellationToken cancellationToken)
    {
        if (id != command.CampaignId)
        {
            command = command with { CampaignId = id };
        }

        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/transition")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> Transition(Guid id, [FromBody] TransitionCampaignStatusCommand command, CancellationToken cancellationToken)
    {
        if (id != command.CampaignId)
        {
            command = command with { CampaignId = id };
        }

        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/stop")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> Stop(Guid id, [FromBody] StopCampaignCommand command, CancellationToken cancellationToken)
    {
        if (id != command.CampaignId)
        {
            command = command with { CampaignId = id };
        }

        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/end")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> End(Guid id, [FromBody] EndCampaignCommand command, CancellationToken cancellationToken)
    {
        if (id != command.CampaignId)
        {
            command = command with { CampaignId = id };
        }

        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/override-profitability")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> OverrideProfitability(Guid id, [FromBody] OverrideCampaignProfitabilityCommand command, CancellationToken cancellationToken)
    {
        if (id != command.CampaignId)
        {
            command = command with { CampaignId = id };
        }

        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/reduce-capacity")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> ReduceCapacity(Guid id, [FromBody] ReduceCampaignCapacityCommand command, CancellationToken cancellationToken)
    {
        if (id != command.CampaignId)
        {
            command = command with { CampaignId = id };
        }

        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("capacity-requests/{reviewId:guid}/review")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> ReviewCapacityRequest(Guid reviewId, [FromBody] ReviewCapacityChangeRequestCommand command, CancellationToken cancellationToken)
    {
        if (reviewId != command.ReviewId)
        {
            command = command with { ReviewId = reviewId };
        }

        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }
}
