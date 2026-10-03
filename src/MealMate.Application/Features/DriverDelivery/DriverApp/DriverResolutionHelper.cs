using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

internal static class DriverResolutionHelper
{
    /// <summary>
    /// All of the driver's trips with an active assignment, newest first, excluding
    /// completed/cancelled ones. A driver can hold several active trips at once
    /// (the dispatcher adds stops to an existing trip or opens new ones), so every
    /// driver-facing list must aggregate across these — filtering to the single
    /// newest trip hides loaded boxes and newly assigned stops behind it.
    /// </summary>
    public static async Task<List<Trip>> GetActiveTripsAsync(
        IAppDbContext db,
        Guid driverProfileId,
        CancellationToken cancellationToken)
    {
        return await (
            from da in db.DriverAssignments
            join t in db.Trips on da.TripId equals t.Id
            where da.DriverProfileId == driverProfileId
                  && da.Status == "Active"
                  && t.Status != TripStatus.Completed
                  && t.Status != TripStatus.Cancelled
            orderby t.CreatedAtUtc descending
            select t
        ).ToListAsync(cancellationToken);
    }

    /// <summary>Stops of the given trips (newest trip first), each trip in sequence order.</summary>
    public static List<TripStop> OrderStopsAcrossTrips(List<Trip> trips, List<TripStop> stops)
    {
        var tripOrder = trips
            .Select((trip, index) => (trip.Id, index))
            .ToDictionary(pair => pair.Item1, pair => pair.index);

        return stops
            .OrderBy(s => tripOrder[s.TripId])
            .ThenBy(s => s.SequenceNumber)
            .ToList();
    }
    public static async Task<DriverProfile?> ResolveDriverAsync(
        IAppDbContext db,
        ICurrentUser currentUser,
        Guid? driverId,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId.HasValue)
        {
            var driver = await db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == currentUser.UserId.Value, cancellationToken);
            if (driver is not null)
            {
                // If driverId was also passed, ensure it matches or user is admin
                if (driverId.HasValue && driverId.Value != Guid.Empty && driverId.Value != driver.Id
                    && !currentUser.Roles.Any(AppRoles.IsAdminRole) && !currentUser.HasPermission(PermissionCodes.DriverRegView))
                {
                    return null;
                }
                return driver;
            }

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

            // User is authenticated but not a driver; allow access only if Admin or Dispatcher
            if (driverId.HasValue && driverId.Value != Guid.Empty
                && (currentUser.Roles.Any(AppRoles.IsAdminRole)
                    || currentUser.HasPermission(PermissionCodes.DriverRegView)
                    || currentUser.HasPermission(PermissionCodes.DeliveryDispatchManage)))
            {
                return await db.DriverProfiles.FirstOrDefaultAsync(d => d.Id == driverId.Value, cancellationToken);
            }

            return null;
        }

        // Fallback for internal / unauthenticated test mock contexts where driverId is provided directly
        if (driverId.HasValue && driverId.Value != Guid.Empty)
        {
            return await db.DriverProfiles.FirstOrDefaultAsync(d => d.Id == driverId.Value, cancellationToken);
        }

        return null;
    }
}
