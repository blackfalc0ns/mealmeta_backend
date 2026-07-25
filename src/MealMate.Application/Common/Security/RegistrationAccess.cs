using MealMate.Application.Common.Abstractions;

namespace MealMate.Application.Common.Security;

internal static class RegistrationAccess
{
    public static bool CanView(
        ICurrentUser user,
        Guid? submittedByUserId,
        string viewPermission,
        string approvePermission)
    {
        if (submittedByUserId is { } ownerId && user.UserId is { } uid && ownerId == uid)
        {
            return true;
        }

        if (user.IsInRole(AppRoles.SuperAdmin))
        {
            return true;
        }

        if (!user.Roles.Any(AppRoles.IsAdminRole))
        {
            return false;
        }

        return user.HasPermission(approvePermission) || user.HasPermission(viewPermission);
    }
}
