using MealMate.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

/// <summary>Shared checks for legacy delivery mutations that must not bypass an open custody handover.</summary>
public static class DriverSupportHandoverMutationGuard
{
    public static Task<bool> HasActiveHandoverAsync(IAppDbContext db, Guid tripId, CancellationToken cancellationToken)
        => db.DriverSupportHandovers.AsNoTracking().AnyAsync(x => x.TripId == tripId
            && (x.Status == "Assigned" || x.Status == "Arrived" || x.Status == "Receiving"), cancellationToken);

    public static Task<bool> HasActiveHandoverForStopAsync(IAppDbContext db, Guid tripStopId, CancellationToken cancellationToken)
        => db.DriverSupportHandoverBoxes.AsNoTracking().AnyAsync(x => x.TripStopId == tripStopId
            && (x.Handover.Status == "Assigned" || x.Handover.Status == "Arrived" || x.Handover.Status == "Receiving"), cancellationToken);

    public static Task<bool> HasActiveHandoverForTripAsync(IAppDbContext db, Guid tripId, CancellationToken cancellationToken)
        => HasActiveHandoverAsync(db, tripId, cancellationToken);

    public static Task<bool> HasActiveHoldForStopAsync(IAppDbContext db, Guid tripStopId, CancellationToken cancellationToken)
        => db.DeliveryHolds.AsNoTracking().AnyAsync(x => x.TripStopId == tripStopId && x.Status == "Active"
            && x.ReleasedAtUtc == null && x.ResolvedAtUtc == null, cancellationToken);

    public static Task<bool> IsReservedReplacementAsync(IAppDbContext db, Guid driverProfileId, CancellationToken cancellationToken)
        => db.DriverSupportHandovers.AsNoTracking().AnyAsync(x => x.ReplacementDriverProfileId == driverProfileId
            && (x.Status == "Assigned" || x.Status == "Arrived" || x.Status == "Receiving"), cancellationToken);
}
