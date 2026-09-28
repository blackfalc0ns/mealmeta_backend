using MealMate.Application.Common.Abstractions;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

internal static class DriverResolutionHelper
{
    public static async Task<DriverProfile?> ResolveDriverAsync(
        IAppDbContext db,
        ICurrentUser currentUser,
        Guid? driverId,
        CancellationToken cancellationToken)
    {
        if (driverId.HasValue && driverId.Value != Guid.Empty)
        {
            return await db.DriverProfiles.FirstOrDefaultAsync(d => d.Id == driverId.Value, cancellationToken);
        }

        if (currentUser.UserId.HasValue)
        {
            var driver = await db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == currentUser.UserId.Value, cancellationToken);
            if (driver is not null) return driver;

            var reg = await db.DriverRegistrations
                .Where(r => r.SubmittedByUserId == currentUser.UserId.Value && r.ResultingDriverProfileId != null)
                .OrderByDescending(r => r.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (reg?.ResultingDriverProfileId is { } profileId)
            {
                var fallbackProfile = await db.DriverProfiles
                    .FirstOrDefaultAsync(d => d.Id == profileId, cancellationToken);
                if (fallbackProfile is not null)
                {
                    fallbackProfile.UserId = currentUser.UserId.Value;
                    await db.SaveChangesAsync(cancellationToken);
                    return fallbackProfile;
                }
            }
        }

        return null;
    }
}
