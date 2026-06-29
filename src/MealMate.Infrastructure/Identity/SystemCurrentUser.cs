using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;

namespace MealMate.Infrastructure.Identity;

/// <summary>
/// Non-interactive principal for background jobs and seeding, where there is no
/// HTTP request. Actions are attributed to the system rather than a real user.
/// </summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;

    public Guid? UserId => null;

    public UserType? UserType => Domain.Enums.UserType.Admin;

    public Guid? RestaurantId => null;

    public IReadOnlyCollection<string> Roles => Array.Empty<string>();

    public IReadOnlyCollection<string> Permissions => Array.Empty<string>();

    public bool IsInRole(string role) => false;

    public bool HasPermission(string permission) => false;
}
