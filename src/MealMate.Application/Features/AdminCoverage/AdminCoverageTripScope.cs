using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.AdminCoverage;

internal sealed record AdminCoverageTripRecord(
    Trip Trip,
    Guid? DriverProfileId,
    string RestaurantNameAr,
    string RestaurantNameEn,
    decimal? RestaurantLatitude,
    decimal? RestaurantLongitude,
    IReadOnlyList<TripStop> Stops,
    IReadOnlyList<Guid?> StopRegionIds,
    IReadOnlyList<Guid> AssignedDriverProfileIds);

/// <summary>Loads real trips and applies each admin scope to every delivery stop before list totals or paging.</summary>
internal static class AdminCoverageTripScope
{
    public static async Task<IReadOnlyList<AdminCoverageTripRecord>> LoadAsync(
        IAppDbContext db,
        CoverageAccessContext access,
        DateOnly? plannedDate,
        Guid? tripId,
        Guid? restaurantId,
        MealMate.Domain.Enums.TripStatus? tripStatus,
        CancellationToken cancellationToken)
    {
        Guid? restaurantConstraint = restaurantId;
        if (tripId is Guid requestedTripId)
        {
            var requestedRestaurantId = await db.Trips.AsNoTracking().Where(trip => trip.Id == requestedTripId && !trip.IsDeleted)
                .Select(trip => (Guid?)trip.RestaurantId).FirstOrDefaultAsync(cancellationToken);
            if (requestedRestaurantId is null || (restaurantConstraint is Guid requestedRestaurant && requestedRestaurant != requestedRestaurantId)) return [];
            restaurantConstraint = requestedRestaurantId;
        }

        var countryRegionIds = access.Regions.Select(region => region.Id).ToArray();
        var profileQuery = db.RestaurantProfiles.AsNoTracking()
            .Where(profile => !profile.IsDeleted && !profile.IsArchived
                && (profile.CountryId == access.Country.Id
                    || (profile.CountryId == null && countryRegionIds.Length > 0
                        && ((profile.PrimaryRegionId != null && countryRegionIds.Contains(profile.PrimaryRegionId.Value))
                            || db.RestaurantServiceRegions.Any(link => link.RestaurantId == profile.Id
                                && countryRegionIds.Contains(link.RegionId) && !link.IsDeleted)))));
        if (restaurantConstraint is Guid constrainedRestaurant) profileQuery = profileQuery.Where(profile => profile.Id == constrainedRestaurant);
        var countryRestaurantIds = await profileQuery.Select(profile => profile.Id).ToArrayAsync(cancellationToken);
        if (countryRestaurantIds.Length == 0) return [];

        var query = db.Trips.AsNoTracking().Where(trip => !trip.IsDeleted && countryRestaurantIds.Contains(trip.RestaurantId));
        if (plannedDate is DateOnly date) query = query.Where(trip => trip.PlannedDate == date);
        if (tripId is Guid id) query = query.Where(trip => trip.Id == id);
        if (tripStatus is MealMate.Domain.Enums.TripStatus status) query = query.Where(trip => trip.Status == status);
        var trips = await query.ToListAsync(cancellationToken);
        if (trips.Count == 0) return [];

        var tripIds = trips.Select(trip => trip.Id).ToArray();
        var stops = await db.TripStops.AsNoTracking()
            .Where(stop => tripIds.Contains(stop.TripId) && !stop.IsDeleted)
            .OrderBy(stop => stop.TripId).ThenBy(stop => stop.SequenceNumber).ThenBy(stop => stop.Id)
            .ToListAsync(cancellationToken);
        var ordersByStop = new Dictionary<Guid, RestaurantDailyOrder>();
        var orderIds = stops.Where(stop => stop.DailyOrderId.HasValue).Select(stop => stop.DailyOrderId!.Value).Distinct().ToArray();
        if (orderIds.Length > 0)
        {
            var orders = await db.RestaurantDailyOrders.AsNoTracking()
                .Where(order => orderIds.Contains(order.Id) && !order.IsDeleted)
                .ToListAsync(cancellationToken);
            ordersByStop = orders.ToDictionary(order => order.Id);
        }
        var dayIds = stops.Where(stop => stop.SubscriptionDayId.HasValue).Select(stop => stop.SubscriptionDayId!.Value)
            .Concat(ordersByStop.Values.Select(order => order.SubscriptionDayId)).Distinct().ToArray();
        var daysById = dayIds.Length == 0
            ? new Dictionary<Guid, SubscriptionDay>()
            : (await db.SubscriptionDays.AsNoTracking().Where(day => dayIds.Contains(day.Id) && !day.IsDeleted).ToListAsync(cancellationToken))
                .ToDictionary(day => day.Id);

        var restaurantIds = trips.Select(trip => trip.RestaurantId).Distinct().ToArray();
        var profiles = await db.RestaurantProfiles.AsNoTracking()
            .Where(profile => restaurantIds.Contains(profile.Id) && !profile.IsDeleted && !profile.IsArchived)
            .Select(profile => new RestaurantScopeProfile(profile.Id, profile.CountryId, profile.PrimaryRegionId, profile.TradeNameAr, profile.TradeNameEn, profile.LegalNameAr, profile.LegalNameEn, profile.Latitude, profile.Longitude))
            .ToListAsync(cancellationToken);
        var profileById = profiles.ToDictionary(profile => profile.Id);
        var regionById = access.Regions.ToDictionary(region => region.Id);
        var activeAssignments = await db.DriverAssignments.AsNoTracking()
            .Where(assignment => tripIds.Contains(assignment.TripId) && !assignment.IsDeleted)
            .OrderByDescending(assignment => assignment.AssignedAtUtc)
            .ThenByDescending(assignment => assignment.Id)
            .ToListAsync(cancellationToken);
        var assignmentsByTrip = activeAssignments.GroupBy(assignment => assignment.TripId).ToDictionary(group => group.Key, group => group.ToArray());
        var driverByTrip = activeAssignments.Where(assignment => assignment.Status == "Active").GroupBy(assignment => assignment.TripId)
            .ToDictionary(group => group.Key, group => (Guid?)group.First().DriverProfileId);
        var stopsByTrip = stops.GroupBy(stop => stop.TripId).ToDictionary(group => group.Key, group => (IReadOnlyList<TripStop>)group.ToArray());
        var visible = new List<AdminCoverageTripRecord>();

        foreach (var trip in trips)
        {
            if (!profileById.TryGetValue(trip.RestaurantId, out var restaurant)) continue;
            var tripStops = stopsByTrip.GetValueOrDefault(trip.Id) ?? [];
            var stopRegionIds = tripStops.Select(stop => ResolveStopRegionId(stop, trip.RestaurantId, ordersByStop, daysById, regionById)).ToArray();
            if (!AllowsTrip(access, trip.RestaurantId, stopRegionIds, regionById)) continue;
            var assignedDriverIds = assignmentsByTrip.GetValueOrDefault(trip.Id)?.Select(assignment => assignment.DriverProfileId).Distinct().ToArray() ?? [];
            visible.Add(new AdminCoverageTripRecord(trip, driverByTrip.GetValueOrDefault(trip.Id), restaurant.TradeNameAr,
                restaurant.TradeNameEn, restaurant.Latitude, restaurant.Longitude, tripStops, stopRegionIds, assignedDriverIds));
        }
        return visible;
    }

    public static bool AllStopsBelongToGovernorate(
        AdminCoverageTripRecord trip,
        Guid governorateId,
        IReadOnlyDictionary<Guid, MealMate.Domain.Foundation.Region> regionById)
        => trip.StopRegionIds.Count > 0 && trip.StopRegionIds.All(regionId => regionId is Guid id
            && CoverageAccessContext.IsDescendantOrSelf(regionById.GetValueOrDefault(id), governorateId, regionById));

    private static Guid? ResolveStopRegionId(
        TripStop stop,
        Guid restaurantId,
        IReadOnlyDictionary<Guid, RestaurantDailyOrder> ordersById,
        IReadOnlyDictionary<Guid, SubscriptionDay> daysById,
        IReadOnlyDictionary<Guid, MealMate.Domain.Foundation.Region> regionById)
    {
        RestaurantDailyOrder? order = null;
        if (stop.DailyOrderId is Guid orderId && !ordersById.TryGetValue(orderId, out order)) return null;
        if (order is not null && order.RestaurantId != restaurantId) return null;
        if (order is not null && stop.SubscriptionDayId is Guid directDayId && order.SubscriptionDayId != directDayId) return null;

        var dayId = stop.SubscriptionDayId ?? order?.SubscriptionDayId;
        if (dayId is not Guid resolvedDayId || !daysById.TryGetValue(resolvedDayId, out var day)) return null;
        if (day.RestaurantId is Guid dayRestaurantId && dayRestaurantId != restaurantId) return null;
        if (day.DeliveryBlockRegionId is Guid blockId && day.DeliveryRegionId is Guid deliveryId
            && (blockId != deliveryId && !IsActiveDescendantOrSelf(blockId, deliveryId, regionById))) return Guid.Empty;
        return day.DeliveryBlockRegionId ?? day.DeliveryRegionId;
    }

    private static bool AllowsTrip(
        CoverageAccessContext access,
        Guid restaurantId,
        IReadOnlyList<Guid?> stopRegionIds,
        IReadOnlyDictionary<Guid, MealMate.Domain.Foundation.Region> regionById)
    {
        if (access.IsSuperAdmin) return true;
        var grants = access.Grants.Where(grant => grant.RestaurantId is null || grant.RestaurantId == restaurantId).ToArray();
        // Known canonical regions outside this country's authorized hierarchy must never be
        // rescued by a broad grant. Null means the source data could not be evaluated.
        if (stopRegionIds.Any(regionId => regionId is Guid knownId && !regionById.ContainsKey(knownId))) return false;
        if (grants.Any(grant => grant.IsCountryWide && grant.RegionId is null && grant.RestaurantId is null)) return true;

        var restaurantOnlyGrant = grants.Any(grant => grant.RestaurantId == restaurantId && grant.RegionId is null && !grant.IsCountryWide);
        if (stopRegionIds.Count == 0) return restaurantOnlyGrant;
        if (stopRegionIds.Any(regionId => regionId is null)) return restaurantOnlyGrant;
        if (restaurantOnlyGrant) return true;

        return stopRegionIds.All(stopRegionId => grants.Any(grant => grant.RegionId is Guid scopeRegionId
            && stopRegionId is Guid resolvedRegionId
            && IsActiveDescendantOrSelf(resolvedRegionId, scopeRegionId, regionById)));
    }

    private static bool IsActiveDescendantOrSelf(Guid regionId, Guid scopeRegionId, IReadOnlyDictionary<Guid, MealMate.Domain.Foundation.Region> regions)
    {
        var currentId = regionId;
        var visited = new HashSet<Guid>();
        while (visited.Add(currentId) && regions.TryGetValue(currentId, out var current))
        {
            if (!current.IsActive) return false;
            if (current.Id == scopeRegionId) return true;
            if (current.ParentRegionId is not Guid parentId) return false;
            currentId = parentId;
        }
        return false;
    }

    private sealed record RestaurantScopeProfile(
        Guid Id, Guid? CountryId, Guid? PrimaryRegionId,
        string TradeNameAr, string TradeNameEn, string LegalNameAr, string LegalNameEn, decimal? Latitude, decimal? Longitude);
}
