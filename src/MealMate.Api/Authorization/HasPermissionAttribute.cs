using Microsoft.AspNetCore.Authorization;

namespace MealMate.Api.Authorization;

/// <summary>Requires the caller to have the given permission code (or SuperAdmin).</summary>
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permission)
        => Policy = PermissionPolicyProvider.PolicyPrefix + permission;
}
