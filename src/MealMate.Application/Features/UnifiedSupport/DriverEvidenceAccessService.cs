using MealMate.Application.Common.Abstractions;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.UnifiedSupport;

/// <summary>Resolves a protected evidence key to its persisted restaurant binding before granting staff access.</summary>
public sealed class DriverEvidenceAccessService(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<bool> CanReadAsync(string key, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } actorId) return false;
        var restaurantIds = await ResolveRestaurantIdsAsync(key, cancellationToken);
        if (restaurantIds.Count != 1) return false;
        var restaurantId = restaurantIds.Single();

        if (currentUser.UserType == UserType.Admin)
            return await new SupportCaseAccess(db, currentUser).CanReadRestaurantAsync(restaurantId, cancellationToken);

        if (currentUser.UserType == UserType.DeliveryManager && currentUser.RestaurantId == restaurantId)
            return (await db.GetDeliveryManagerUserIdsAsync(restaurantId, cancellationToken)).Contains(actorId);

        return false;
    }

    private async Task<HashSet<Guid>> ResolveRestaurantIdsAsync(string key, CancellationToken ct)
    {
        var ids = new HashSet<Guid>();
        foreach (var id in await db.Set<ComplaintEvidencePhoto>().AsNoTracking()
                     .Where(x => x.StoragePath == key).Select(x => x.Complaint.RestaurantId).ToListAsync(ct))
            if (id.HasValue) ids.Add(id.Value);
        foreach (var id in await db.PickupScans.AsNoTracking()
                     .Where(x => x.PhotoPath == key).Select(x => (Guid?)x.Trip.RestaurantId).ToListAsync(ct))
            if (id.HasValue) ids.Add(id.Value);
        foreach (var id in await db.PickupConditionPhotos.AsNoTracking()
                     .Where(x => x.StorageKey == key && x.DeletedAtUtc == null)
                     .Select(x => (Guid?)db.Trips.Where(t => t.Id == x.TripId).Select(t => t.RestaurantId).FirstOrDefault())
                     .ToListAsync(ct))
            if (id.HasValue) ids.Add(id.Value);

        var confirmations = await db.DeliveryConfirmationScans.AsNoTracking()
            .Where(x => x.PhotoPath == key)
            .Select(x => new { x.OrderId, x.SubscriptionDayId })
            .ToListAsync(ct);
        foreach (var confirmation in confirmations)
        {
            if (confirmation.OrderId is not { } orderId && confirmation.SubscriptionDayId is not { } dayId) continue;
            var tripRestaurantIds = await db.TripStops.AsNoTracking()
                .Where(stop => (confirmation.OrderId != null && stop.DailyOrderId == confirmation.OrderId)
                    || (confirmation.SubscriptionDayId != null && stop.SubscriptionDayId == confirmation.SubscriptionDayId))
                .Select(stop => stop.Trip.RestaurantId).Distinct().ToListAsync(ct);
            foreach (var id in tripRestaurantIds) ids.Add(id);
        }
        return ids;
    }
}
