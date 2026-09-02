using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.CustomerPush;
using MealMate.Application.Features.IdentityAccess.AdminUsers;
using MealMate.Application.Features.IdentityAccess.DriverRegistration;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/admin/users")]
public sealed class AdminUsersController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.AdminUsersView)]
    public async Task<IActionResult> List(
        [FromQuery] string? search = null,
        [FromQuery] UserType? userType = null,
        [FromQuery] AccountStatus? status = null,
        [FromQuery] string? role = null,
        [FromQuery] bool? twoFactorEnabled = null,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new ListAdminUsersQuery(search, userType, status, role, twoFactorEnabled), cancellationToken));

    [HttpPost("invite")]
    [HasPermission(PermissionCodes.AdminUsersInvite)]
    public async Task<IActionResult> Invite([FromBody] InviteAdminCommand body, CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(body, cancellationToken), "/api/v1/admin/users");

    [HttpPost("{userId:guid}/status")]
    [HasPermission(PermissionCodes.AdminUsersManage)]
    public async Task<IActionResult> SetStatus(Guid userId, [FromBody] SetStatusRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SetAdminStatusCommand(userId, body.Status, body.Reason), cancellationToken));

    [HttpPost("{userId:guid}/reset-2fa")]
    [Authorize(Roles = AppRoles.SuperAdmin)]
    public async Task<IActionResult> Reset2Fa(Guid userId, [FromBody] Reset2FaRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ResetAdmin2FaCommand(userId, body.Reason), cancellationToken));

    [HttpGet("{userId:guid}/driver-registration")]
    [HasPermission(PermissionCodes.DriverRegView)]
    public async Task<IActionResult> GetDriverRegistration(Guid userId, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetDriverRegistrationByUserIdQuery(userId), cancellationToken));

    [HttpPost("{userId:guid}/push-test")]
    [HasPermission(PermissionCodes.CustomersView)]
    public async Task<IActionResult> SendCustomerTestPush(Guid userId, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SendAdminCustomerTestPushCommand(userId), cancellationToken));
}

[Route("api/v1/admin/invitations")]
[EnableRateLimiting("auth")]
public sealed class AdminInvitationsController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.AdminUsersView)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ListAdminInvitationsQuery(), cancellationToken));

    [HttpPost("{userId:guid}/resend")]
    [HasPermission(PermissionCodes.AdminUsersInvite)]
    public async Task<IActionResult> Resend(
        Guid userId,
        [FromBody] InvitationReasonRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ResendAdminInvitationCommand(userId, body.Reason), cancellationToken));

    [HttpDelete("{userId:guid}")]
    [HasPermission(PermissionCodes.AdminUsersManage)]
    public async Task<IActionResult> Revoke(
        Guid userId,
        [FromQuery] string reason,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new RevokeAdminInvitationCommand(userId, reason), cancellationToken));

    [HttpPost("accept")]
    [AllowAnonymous]
    public async Task<IActionResult> Accept([FromBody] AcceptAdminInviteCommand body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(body, cancellationToken));
}

public sealed record SetStatusRequest(AccountStatus Status, string Reason);
public sealed record Reset2FaRequest(string Reason);
public sealed record InvitationReasonRequest(string Reason);
