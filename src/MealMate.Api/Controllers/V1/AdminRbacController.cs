using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Rbac;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/admin")]
public sealed class AdminRbacController : ApiControllerBase
{
    [HttpGet("roles")]
    [HasPermission(PermissionCodes.RolesView)]
    public async Task<IActionResult> ListRoles(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ListRolesQuery(), cancellationToken));

    [HttpGet("permissions")]
    [HasPermission(PermissionCodes.RolesView)]
    public async Task<IActionResult> ListPermissions(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ListPermissionsQuery(), cancellationToken));

    [HttpGet("scopes")]
    [HasPermission(PermissionCodes.ScopesManage)]
    public async Task<IActionResult> ListScopes(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ListAccessScopesQuery(), cancellationToken));

    [HttpPost("roles")]
    [HasPermission(PermissionCodes.RolesManage)]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest body, CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(new CreateRoleCommand(body.Name, body.Description, body.PermissionCodes), cancellationToken), "/api/v1/admin/roles");

    [HttpPut("roles/{id:guid}/permissions")]
    [HasPermission(PermissionCodes.RolesManage)]
    public async Task<IActionResult> UpdateRolePermissions(Guid id, [FromBody] UpdateRolePermissionsRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UpdateRolePermissionsCommand(id, body.PermissionCodes, body.Reason), cancellationToken));

    [HttpPost("users/{userId:guid}/roles")]
    [HasPermission(PermissionCodes.UsersRolesAssign)]
    public async Task<IActionResult> AssignRole(Guid userId, [FromBody] AssignRoleRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AssignUserRoleCommand(userId, body.RoleName, body.Reason), cancellationToken));

    [HttpDelete("users/{userId:guid}/roles/{roleName}")]
    [HasPermission(PermissionCodes.UsersRolesAssign)]
    public async Task<IActionResult> RemoveRole(Guid userId, string roleName, [FromQuery] string reason, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new RemoveUserRoleCommand(userId, roleName, reason), cancellationToken));

    [HttpPost("users/{userId:guid}/scopes")]
    [HasPermission(PermissionCodes.ScopesManage)]
    public async Task<IActionResult> AssignScope(Guid userId, [FromBody] AssignScopeRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AssignUserScopeCommand(userId, body.AccessScopeId, body.Reason), cancellationToken));

    [HttpDelete("users/{userId:guid}/scopes/{scopeId:guid}")]
    [HasPermission(PermissionCodes.ScopesManage)]
    public async Task<IActionResult> RemoveScope(
        Guid userId,
        Guid scopeId,
        [FromQuery] string reason,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new RemoveUserScopeCommand(userId, scopeId, reason), cancellationToken));
}

public sealed record CreateRoleRequest(string Name, string? Description, IReadOnlyList<string>? PermissionCodes);
public sealed record UpdateRolePermissionsRequest(IReadOnlyList<string> PermissionCodes, string Reason);
public sealed record AssignRoleRequest(string RoleName, string Reason);
public sealed record AssignScopeRequest(Guid AccessScopeId, string Reason);
