using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[Route("api/v1/customer/family-subscription-flow")]
public sealed class CustomerFamilySubscriptionFlowController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;

    public CustomerFamilySubscriptionFlowController(ICurrentUser currentUser)
        => _currentUser = currentUser;

    [HttpGet("metadata")]
    [ProducesResponseType(typeof(FamilyDraftMetadataDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Metadata(CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetFamilyDraftMetadataQuery(), cancellationToken));
    }

    [HttpGet("current")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Current(CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetCurrentFamilyGroupQuery(), cancellationToken));
    }

    [HttpPut("group")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertGroup([FromBody] UpsertFamilyGroupRequest request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new UpsertFamilyGroupCommand(request.Name), cancellationToken));
    }

    [HttpPost("members")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddMember([FromBody] FamilyMemberWriteDto request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new AddFamilyDraftMemberCommand(request), cancellationToken));
    }

    [HttpPut("members/{memberId:guid}")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMember(Guid memberId, [FromBody] FamilyMemberWriteDto request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new UpdateFamilyMemberCommand(memberId, request), cancellationToken));
    }

    [HttpDelete("members/{memberId:guid}")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteMember(Guid memberId, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new DeleteFamilyMemberCommand(memberId), cancellationToken));
    }

    [HttpPut("members/{memberId:guid}/plan")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetMemberPlan(
        Guid memberId,
        [FromBody] SetFamilyMemberPlanRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new SetFamilyMemberPlanCommand(memberId, request), cancellationToken));
    }

    private bool IsCustomer() => _currentUser.UserType == UserType.Customer && _currentUser.UserId.HasValue;
}
