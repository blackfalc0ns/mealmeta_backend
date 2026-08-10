using MealMate.Api.Common;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurant/branches")]
public sealed class RestaurantBranchesController : ApiControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> List([FromQuery] Guid? restaurantId, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new ListRestaurantBranchesQuery(restaurantId ?? Guid.Empty), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Create([FromBody] CreateRestaurantBranchRequest request, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new CreateRestaurantBranchCommand(
            request.RestaurantRegistrationId,
            request.BranchCode ?? "",
            request.NameAr,
            request.NameEn ?? request.NameAr,
            request.AddressAr,
            request.AddressEn ?? request.AddressAr,
            request.GovernorateAr,
            request.GovernorateEn ?? request.GovernorateAr,
            request.ContactPhone,
            request.IsPrimary,
            request.ManagerStaffId,
            request.PhotoStorageKey), cancellationToken);

        return HandleResult(result);
    }

    [HttpPost("{id:guid}/toggle")]
    [AllowAnonymous]
    public async Task<IActionResult> ToggleStatus(Guid id, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new ToggleBranchStatusCommand(id), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("invites")]
    [AllowAnonymous]
    public async Task<IActionResult> ListInvites([FromQuery] Guid? restaurantId, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new ListStaffInvitationsQuery(restaurantId ?? Guid.Empty), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("staff")]
    [AllowAnonymous]
    public async Task<IActionResult> ListStaff([FromQuery] Guid? restaurantId, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new ListRestaurantStaffQuery(restaurantId ?? Guid.Empty), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("permissions")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPermissions([FromQuery] Guid? restaurantId, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetRolePermissionsQuery(restaurantId ?? Guid.Empty), cancellationToken);
        return HandleResult(result);
    }

    [HttpPut("permissions")]
    [AllowAnonymous]
    public async Task<IActionResult> UpdatePermission([FromBody] UpdatePermissionRequest request, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new UpdateRolePermissionCommand(
            request.RestaurantRegistrationId,
            request.RoleId,
            request.ModuleKey,
            request.PermissionLevel), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("roles")]
    [AllowAnonymous]
    public async Task<IActionResult> CreateRole([FromBody] CreateRestaurantRoleRequest request, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new CreateCustomRoleCommand(
            request.RestaurantRegistrationId,
            request.NameAr,
            request.NameEn,
            request.DescriptionAr,
            request.DescriptionEn), cancellationToken);
        return HandleResult(result);
    }

    [HttpDelete("roles/{roleId}")]
    [AllowAnonymous]
    public async Task<IActionResult> DeleteRole([FromQuery] Guid? restaurantId, string roleId, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new DeleteCustomRoleCommand(restaurantId ?? Guid.Empty, roleId), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("invites")]
    [AllowAnonymous]
    public async Task<IActionResult> SendInvite([FromBody] SendStaffInviteRequest request, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new SendStaffInviteCommand(
            request.RestaurantRegistrationId,
            request.FullName,
            request.Email,
            request.Phone,
            request.RoleId,
            request.BranchId,
            request.PreferredLanguage), cancellationToken);

        return HandleResult(result);
    }

    [HttpPost("invites/accept")]
    [AllowAnonymous]
    public async Task<IActionResult> AcceptInvite([FromBody] AcceptStaffInviteRequest request, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new AcceptStaffInviteCommand(
            request.Email,
            request.InviteToken,
            request.Password), cancellationToken);

        return HandleResult(result);
    }

    [HttpPost("invites/{inviteId:guid}/resend")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendInvite(Guid inviteId, [FromBody] ResendStaffInviteRequest? request, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new ResendStaffInviteCommand(
            inviteId,
            request?.PreferredLanguage), cancellationToken);

        return HandleResult(result);
    }
}

public sealed record CreateRestaurantBranchRequest(
    Guid RestaurantRegistrationId,
    string? BranchCode,
    string NameAr,
    string? NameEn,
    string AddressAr,
    string? AddressEn,
    string GovernorateAr,
    string? GovernorateEn,
    string ContactPhone,
    bool IsPrimary = false,
    Guid? ManagerStaffId = null,
    string? PhotoStorageKey = null);

public sealed record SendStaffInviteRequest(
    Guid RestaurantRegistrationId,
    string FullName,
    string Email,
    string Phone,
    string RoleId,
    string BranchId,
    string? PreferredLanguage = null);

public sealed record AcceptStaffInviteRequest(
    string Email,
    string InviteToken,
    string Password);

public sealed record ResendStaffInviteRequest(
    string? PreferredLanguage = null);

public sealed record UpdatePermissionRequest(
    Guid RestaurantRegistrationId,
    string RoleId,
    string ModuleKey,
    string PermissionLevel);

public sealed record CreateRestaurantRoleRequest(
    Guid RestaurantRegistrationId,
    string NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn);
