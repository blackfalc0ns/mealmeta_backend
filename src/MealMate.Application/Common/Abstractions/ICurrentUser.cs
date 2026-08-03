using MealMate.Domain.Enums;

namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// The authenticated principal for the current request. Never trust client-supplied
/// identity; this is resolved from the validated token at the boundary.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    UserType? UserType { get; }

    /// <summary>Restaurant the user acts for, when applicable (ownership checks).</summary>
    Guid? RestaurantId { get; }

    IReadOnlyCollection<string> Roles { get; }

    IReadOnlyCollection<string> Permissions { get; }

    /// <summary>Access scope IDs parsed from the token claims.</summary>
    IReadOnlyCollection<Guid> AccessScopeIds { get; }

    /// <summary>Tenant IDs parsed from the token claims.</summary>
    IReadOnlyCollection<Guid> TenantIds { get; }

    bool IsInRole(string role);

    bool HasPermission(string permission);
}
