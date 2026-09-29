using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Application.Features.RestaurantOperations;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurant/branches")]
[Authorize]
public sealed class RestaurantBranchesController : ApiControllerBase
{
    private ICurrentUser CurrentUser => HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? restaurantId, CancellationToken cancellationToken)
    {
        var scopeResult = RestaurantOperationsScope.ResolveRestaurantId(CurrentUser, restaurantId);
        if (scopeResult.IsFailure)
        {
            return HandleResult(Result.Failure<IReadOnlyList<RestaurantBranchDto>>(scopeResult.Error));
        }

        var result = await Sender.Send(new ListRestaurantBranchesQuery(scopeResult.Value), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRestaurantBranchRequest request, CancellationToken cancellationToken)
    {
        if (!IsAuthorizedToManageTeam(CurrentUser))
        {
            return HandleResult(Result.Failure<RestaurantBranchDto>(Error.Forbidden("restaurant.forbidden", "Only restaurant owners, admins, or authorized staff may manage branches.")));
        }

        var scopeResult = RestaurantOperationsScope.ResolveRestaurantId(
            CurrentUser,
            request.RestaurantRegistrationId == Guid.Empty ? null : request.RestaurantRegistrationId);

        if (scopeResult.IsFailure)
        {
            return HandleResult(Result.Failure<RestaurantBranchDto>(scopeResult.Error));
        }

        var result = await Sender.Send(new CreateRestaurantBranchCommand(
            scopeResult.Value,
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
    public async Task<IActionResult> ToggleStatus(Guid id, CancellationToken cancellationToken)
    {
        if (!IsAuthorizedToManageTeam(CurrentUser))
        {
            return HandleResult(Result.Failure<bool>(Error.Forbidden("restaurant.forbidden", "Only restaurant owners, admins, or authorized staff may toggle branch status.")));
        }

        var result = await Sender.Send(new ToggleBranchStatusCommand(id), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("invites")]
    public async Task<IActionResult> ListInvites([FromQuery] Guid? restaurantId, CancellationToken cancellationToken)
    {
        var scopeResult = RestaurantOperationsScope.ResolveRestaurantId(CurrentUser, restaurantId);
        if (scopeResult.IsFailure)
        {
            return HandleResult(Result.Failure<IReadOnlyList<StaffInvitationDto>>(scopeResult.Error));
        }

        var result = await Sender.Send(new ListStaffInvitationsQuery(scopeResult.Value), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("staff")]
    public async Task<IActionResult> ListStaff([FromQuery] Guid? restaurantId, CancellationToken cancellationToken)
    {
        var scopeResult = RestaurantOperationsScope.ResolveRestaurantId(CurrentUser, restaurantId);
        if (scopeResult.IsFailure)
        {
            return HandleResult(Result.Failure<IReadOnlyList<RestaurantStaffDto>>(scopeResult.Error));
        }

        var result = await Sender.Send(new ListRestaurantStaffQuery(scopeResult.Value), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("permissions")]
    public async Task<IActionResult> GetPermissions([FromQuery] Guid? restaurantId, CancellationToken cancellationToken)
    {
        var scopeResult = RestaurantOperationsScope.ResolveRestaurantId(CurrentUser, restaurantId);
        if (scopeResult.IsFailure)
        {
            return HandleResult(Result.Failure<RestaurantPermissionsDto>(scopeResult.Error));
        }

        var result = await Sender.Send(new GetRolePermissionsQuery(scopeResult.Value), cancellationToken);
        return HandleResult(result);
    }

    [HttpPut("permissions")]
    public async Task<IActionResult> UpdatePermission([FromBody] UpdatePermissionRequest request, CancellationToken cancellationToken)
    {
        if (!IsAuthorizedToManageTeam(CurrentUser))
        {
            return HandleResult(Result.Failure<RolePermissionEntryDto>(Error.Forbidden("restaurant.forbidden", "Only restaurant owners or admins may update permissions.")));
        }

        var scopeResult = RestaurantOperationsScope.ResolveRestaurantId(
            CurrentUser,
            request.RestaurantRegistrationId == Guid.Empty ? null : request.RestaurantRegistrationId);

        if (scopeResult.IsFailure)
        {
            return HandleResult(Result.Failure<RolePermissionEntryDto>(scopeResult.Error));
        }

        var result = await Sender.Send(new UpdateRolePermissionCommand(
            scopeResult.Value,
            request.RoleId,
            request.ModuleKey,
            request.PermissionLevel), cancellationToken);

        return HandleResult(result);
    }

    [HttpPost("roles")]
    public async Task<IActionResult> CreateRole([FromBody] CreateRestaurantRoleRequest request, CancellationToken cancellationToken)
    {
        if (!IsAuthorizedToManageTeam(CurrentUser))
        {
            return HandleResult(Result.Failure<RoleDefinitionDto>(Error.Forbidden("restaurant.forbidden", "Only restaurant owners or admins may create roles.")));
        }

        var scopeResult = RestaurantOperationsScope.ResolveRestaurantId(
            CurrentUser,
            request.RestaurantRegistrationId == Guid.Empty ? null : request.RestaurantRegistrationId);

        if (scopeResult.IsFailure)
        {
            return HandleResult(Result.Failure<RoleDefinitionDto>(scopeResult.Error));
        }

        var result = await Sender.Send(new CreateCustomRoleCommand(
            scopeResult.Value,
            request.NameAr,
            request.NameEn,
            request.DescriptionAr,
            request.DescriptionEn), cancellationToken);

        return HandleResult(result);
    }

    [HttpDelete("roles/{roleId}")]
    public async Task<IActionResult> DeleteRole([FromQuery] Guid? restaurantId, string roleId, CancellationToken cancellationToken)
    {
        if (!IsAuthorizedToManageTeam(CurrentUser))
        {
            return HandleResult(Result.Failure(Error.Forbidden("restaurant.forbidden", "Only restaurant owners or admins may delete roles.")));
        }

        var scopeResult = RestaurantOperationsScope.ResolveRestaurantId(CurrentUser, restaurantId);
        if (scopeResult.IsFailure)
        {
            return HandleResult(Result.Failure(scopeResult.Error));
        }

        var result = await Sender.Send(new DeleteCustomRoleCommand(scopeResult.Value, roleId), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("invites")]
    public async Task<IActionResult> SendInvite([FromBody] SendStaffInviteRequest request, CancellationToken cancellationToken)
    {
        if (!IsAuthorizedToManageTeam(CurrentUser))
        {
            return HandleResult(Result.Failure<StaffInvitationDto>(Error.Forbidden("restaurant.forbidden", "Only restaurant owners or authorized staff may send invitations.")));
        }

        var scopeResult = RestaurantOperationsScope.ResolveRestaurantId(
            CurrentUser,
            request.RestaurantRegistrationId == Guid.Empty ? null : request.RestaurantRegistrationId);

        if (scopeResult.IsFailure)
        {
            return HandleResult(Result.Failure<StaffInvitationDto>(scopeResult.Error));
        }

        var result = await Sender.Send(new SendStaffInviteCommand(
            scopeResult.Value,
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
    public async Task<IActionResult> ResendInvite(Guid inviteId, [FromBody] ResendStaffInviteRequest? request, CancellationToken cancellationToken)
    {
        if (!IsAuthorizedToManageTeam(CurrentUser))
        {
            return HandleResult(Result.Failure<bool>(Error.Forbidden("restaurant.forbidden", "Only restaurant owners or authorized staff may resend invitations.")));
        }

        var result = await Sender.Send(new ResendStaffInviteCommand(
            inviteId,
            request?.PreferredLanguage), cancellationToken);

        return HandleResult(result);
    }

    private static bool IsAuthorizedToManageTeam(ICurrentUser user)
    {
        if (!user.IsAuthenticated) return false;
        if (user.UserType == UserType.Admin) return true;
        if (user.IsInRole(AppRoles.SuperAdmin) || user.IsInRole(AppRoles.OperationsAdmin)) return true;
        if (user.HasPermission(PermissionCodes.RestaurantRegApprove) || user.HasPermission(PermissionCodes.AdminUsersManage)) return true;
        if (user.IsInRole(AppRoles.RestaurantOwner) || user.IsInRole("owner") || user.IsInRole("Owner") ||
            user.Roles.Any(r => string.Equals(r, "owner", StringComparison.OrdinalIgnoreCase) || string.Equals(r, AppRoles.RestaurantOwner, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        if (user.HasPermission("team.manage") || user.HasPermission("team:manage")) return true;
        return false;
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
