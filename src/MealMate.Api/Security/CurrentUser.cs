using System.Security.Claims;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;

namespace MealMate.Api.Security;

/// <summary>
/// Resolves the authenticated principal from the validated JWT on the current
/// request. Never reads identity from the request body or headers directly.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    private readonly ClaimsPrincipal? _principal;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
        => _principal = httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => _principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId =>
        Guid.TryParse(_principal?.FindFirstValue(AppClaimTypes.UserId), out var id) ? id : null;

    public UserType? UserType =>
        Enum.TryParse<UserType>(_principal?.FindFirstValue(AppClaimTypes.UserType), out var type)
            ? type
            : null;

    public Guid? RestaurantId =>
        Guid.TryParse(_principal?.FindFirstValue(AppClaimTypes.RestaurantId), out var id) ? id : null;

    public IReadOnlyCollection<string> Roles =>
        _principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray() ?? Array.Empty<string>();

    public IReadOnlyCollection<string> Permissions =>
        _principal?.FindAll(AppClaimTypes.Permission).Select(c => c.Value).ToArray() ?? Array.Empty<string>();

    public bool IsInRole(string role) => _principal?.IsInRole(role) ?? false;

    public bool HasPermission(string permission) => Permissions.Contains(permission);
}
