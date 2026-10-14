using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Features.FamilyManager;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/customer/family")]
[Authorize]
public sealed class CustomerFamilyManagerController : ApiControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> GetMyContext(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetMyFamilySessionContextQuery(), cancellationToken));

    [HttpGet("home")]
    public async Task<IActionResult> GetHome(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetFamilyHomeQuery(), cancellationToken));

    [HttpGet("members")]
    public async Task<IActionResult> GetMembers(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetFamilyMembersQuery(), cancellationToken));

    [HttpGet("switcher")]
    public async Task<IActionResult> GetSwitcher(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetFamilySwitcherQuery(), cancellationToken));

    [HttpGet("members/switcher")]
    public async Task<IActionResult> GetMembersSwitcher(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetFamilySwitcherQuery(), cancellationToken));

    [HttpGet("members/{memberId:guid}")]
    public async Task<IActionResult> GetMember(Guid memberId, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetFamilyMemberQuery(memberId), cancellationToken));

    [HttpPost("members")]
    public async Task<IActionResult> CreateMember(
        [FromBody] CreateFamilyMemberRequest request,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new CreateFamilyMemberCommand(request), cancellationToken));

    [HttpGet("members/{memberId:guid}/capabilities")]
    public async Task<IActionResult> GetCapabilities(Guid memberId, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetMyFamilyCapabilitiesQuery(memberId), cancellationToken));

    [HttpGet("members/{memberId:guid}/access")]
    public async Task<IActionResult> GetAccess(Guid memberId, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetFamilyMemberAccessQuery(memberId), cancellationToken));

    [HttpPut("members/{memberId:guid}/access")]
    public async Task<IActionResult> UpdateAccess(
        Guid memberId,
        [FromBody] UpdateFamilyMemberAccessRequest request,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UpdateFamilyMemberAccessCommand(memberId, request), cancellationToken));

    [HttpGet("members/{memberId:guid}/permissions")]
    public async Task<IActionResult> GetPermissions(Guid memberId, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetFamilyMemberPermissionsQuery(memberId), cancellationToken));

    [HttpPut("members/{memberId:guid}/permissions")]
    public async Task<IActionResult> UpdatePermissions(
        Guid memberId,
        [FromBody] UpdateFamilyMemberPermissionsRequest request,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UpdateFamilyMemberPermissionsCommand(memberId, request), cancellationToken));

    [HttpPost("invites/{token}/accept")]
    public async Task<IActionResult> AcceptInvite(string token, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AcceptFamilyInviteCommand(token), cancellationToken));
}
