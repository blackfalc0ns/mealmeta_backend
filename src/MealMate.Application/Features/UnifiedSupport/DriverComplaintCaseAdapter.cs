using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.UnifiedSupport;

/// <summary>Reads only driver-originated complaints already supported by the driver app.</summary>
internal sealed class DriverComplaintCaseAdapter(IAppDbContext db, ICurrentUser user, SupportCaseAccess access)
{
    public async Task<IQueryable<Complaint>> QueryVisibleAsync(CancellationToken ct)
    {
        var query = db.Complaints.AsNoTracking().Where(c => c.DriverProfileId != null
            && DriverSupportIssueProjection.SupportedCategoryValues.Contains(c.Category));
        if (!user.IsAuthenticated || user.UserId is null) return query.Where(_ => false);
        if (user.UserType == UserType.Driver)
        {
            if (!await access.HasActiveDriverIdentityAsync(ct)) return query.Where(_ => false);
            var profileId = await db.DriverProfiles.AsNoTracking().Where(x => x.UserId == user.UserId && !x.IsDeleted)
                .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
            return profileId is null ? query.Where(_ => false) : query.Where(c => c.DriverProfileId == profileId);
        }

        if (user.UserType == UserType.DeliveryManager && user.RestaurantId is { } restaurantId)
        {
            var managers = await db.GetDeliveryManagerUserIdsAsync(restaurantId, ct);
            return managers.Contains(user.UserId.Value) ? query.Where(c => c.RestaurantId == restaurantId) : query.Where(_ => false);
        }

        if (user.UserType != UserType.Admin || !access.IsSupportAdmin && !access.IsSuperAdmin)
            return query.Where(_ => false);
        var scope = await access.GetOperationalRestaurantScopeAsync(ct);
        return scope.IsGlobal ? query : query.Where(c => c.RestaurantId.HasValue && scope.RestaurantIds.Contains(c.RestaurantId.Value));
    }
}
