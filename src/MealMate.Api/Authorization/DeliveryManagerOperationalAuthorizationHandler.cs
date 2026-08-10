using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Authorization;

/// <summary>
/// Re-validates delivery-manager restaurant access for every authorized request.
/// This closes the window where an already-issued stateless access token could
/// otherwise continue to operate after the restaurant is suspended.
/// </summary>
public sealed class DeliveryManagerOperationalAuthorizationHandler : IAuthorizationHandler
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;

    public DeliveryManagerOperationalAuthorizationHandler(
        ICurrentUser currentUser,
        IUserAccountService users,
        IAppDbContext db)
    {
        _currentUser = currentUser;
        _users = users;
        _db = db;
    }

    public async Task HandleAsync(AuthorizationHandlerContext context)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserType != UserType.DeliveryManager)
        {
            return;
        }

        if (_currentUser.UserId is not Guid userId
            || _currentUser.RestaurantId is not Guid restaurantId)
        {
            context.Fail(new AuthorizationFailureReason(this, "Delivery manager is not linked to a restaurant."));
            return;
        }

        var user = await _users.FindByIdAsync(userId);
        if (user is null
            || user.UserType != UserType.DeliveryManager
            || user.AccountStatus != AccountStatus.Active
            || user.RestaurantId != restaurantId)
        {
            context.Fail(new AuthorizationFailureReason(this, "Delivery manager account is not active."));
            return;
        }

        var restaurantCanOperate = await _db.RestaurantProfiles
            .AsNoTracking()
            .AnyAsync(profile => profile.Id == restaurantId
                && profile.VerificationStatus == VerificationStatus.Approved
                && profile.IsOperationallyActive);

        if (!restaurantCanOperate)
        {
            context.Fail(new AuthorizationFailureReason(this, "Restaurant is not operational."));
        }
    }
}
