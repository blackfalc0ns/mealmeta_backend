using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;

namespace MealMate.Api.Authorization;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission) => Permission = permission;

    public string Permission { get; }
}

/// <summary>
/// Succeeds when the caller is SuperAdmin or carries the required permission claim.
/// Scope checks for country/restaurant resources are enforced inside handlers when needed.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly ICurrentUser _currentUser;

    public PermissionAuthorizationHandler(ICurrentUser currentUser)
        => _currentUser = currentUser;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Task.CompletedTask;
        }

        if (_currentUser.IsInRole(AppRoles.SuperAdmin)
            || _currentUser.HasPermission(requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
