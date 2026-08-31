using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.RestaurantOperations;

public static class RestaurantOperationsScope
{
    public static Result<Guid> ResolveRestaurantId(ICurrentUser user, Guid? overrideRestaurantId)
    {
        if (overrideRestaurantId is { } explicitId)
        {
            if (user.UserType != UserType.Admin
                && !user.HasPermission(PermissionCodes.RestaurantRegView)
                && !user.IsInRole(AppRoles.SuperAdmin)
                && !user.IsInRole(AppRoles.OperationsAdmin))
            {
                return Result.Failure<Guid>(Error.Forbidden(
                    "restaurant.admin_scope_required",
                    "Admin scope is required to manage another restaurant."));
            }

            return explicitId;
        }

        if (!user.RestaurantId.HasValue)
        {
            return Result.Failure<Guid>(Error.Unauthorized(
                "restaurant.id_required",
                "Restaurant identity is required."));
        }

        return user.RestaurantId.Value;
    }
}
