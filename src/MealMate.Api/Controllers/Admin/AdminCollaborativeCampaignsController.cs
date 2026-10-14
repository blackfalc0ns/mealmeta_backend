using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.Queries;
using MealMate.Domain.Enums;
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

    [HttpPost("pricing-policy")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> ApprovePricingPolicy([FromBody] ApproveCampaignPricingPolicyCommand command, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        return HandleCreated(result, $"api/v1/admin/campaigns/pricing-policy/{result.Value?.Id}");
    }

    [HttpGet("pricing-policy")]
    [HasPermission(PermissionCodes.CampaignsView)]
    public async Task<IActionResult> GetPricingPolicy(CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetCampaignPricingPolicyQuery(), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/reschedule-review")]
    [HasPermission(PermissionCodes.CampaignsManage)]
    public async Task<IActionResult> RescheduleReview(Guid id, [FromBody] RescheduleCampaignReviewCommand command, CancellationToken cancellationToken)
    {
        if (id != command.CampaignId)
        {
            command = command with { CampaignId = id };
        }

        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet]
    [HasPermission(PermissionCodes.CampaignsView)]
    public async Task<IActionResult> List([FromQuery] CampaignStatus? status, [FromQuery] PageRequest? page, [FromQuery] string? search, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetCampaignsListQuery(status, page, search), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.CampaignsView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetCampaignByIdQuery(id), cancellationToken);
        return HandleResult(result);
    }
}
