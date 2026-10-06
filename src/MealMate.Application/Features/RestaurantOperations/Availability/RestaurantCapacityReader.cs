using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.RestaurantOperations.Availability;

/// <summary>
/// EF implementation of the unified capacity accounting. Ghost holds (Confirmed holds
/// whose subscription is cancelled/upgraded/payment-failed) no longer consume capacity
/// anywhere, and hold-less days (auto-selection, completed campaigns, admin reassignment
/// without a hold) count exactly like identities that carry a hold.
/// </summary>
public sealed class RestaurantCapacityReader : IRestaurantCapacityReader
{
    /// <summary>Subscription statuses whose holds are residue, not live commitments.</summary>
    private static readonly IReadOnlyList<SubscriptionStatus> GhostSubscriptionStatuses =
    [
        SubscriptionStatus.Cancelled,
        SubscriptionStatus.Upgraded,
        SubscriptionStatus.PaymentFailed,
    ];

    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public RestaurantCapacityReader(IAppDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyDictionary<(Guid RestaurantId, DateOnly ServiceDate), RestaurantCapacityUnits>> ReadAsync(
        IReadOnlyCollection<(Guid RestaurantId, DateOnly ServiceDate)> keys, CancellationToken cancellationToken = default)
    {
        var distinctKeys = keys.Distinct().ToArray();
        if (distinctKeys.Length == 0)
        {
            return new Dictionary<(Guid, DateOnly), RestaurantCapacityUnits>();
        }

        var restaurantIds = distinctKeys.Select(x => x.RestaurantId).Distinct().ToList();
        var dates = distinctKeys.Select(x => x.ServiceDate).Distinct().ToList();
        var maxDate = dates.Max();
        var nowUtc = _clock.UtcNow;

        var dayIdentities = await _db.SubscriptionDays.AsNoTracking()
            .Where(d => d.RestaurantId != null
                && restaurantIds.Contains(d.RestaurantId.Value)
                && dates.Contains(d.ServiceDate)
                && d.Status != SubscriptionDayStatus.Cancelled
                && !d.IsDeleted)
            .Select(d => new { d.RestaurantId, d.ServiceDate, d.SubscriptionId })
            .ToListAsync(cancellationToken);

        var holdRows = await (
            from hold in _db.CapacityHolds.AsNoTracking()
            join subscriptionJoin in _db.Subscriptions.AsNoTracking() on hold.SubscriptionId equals subscriptionJoin.Id into subscriptionBatches
            from subscription in subscriptionBatches.DefaultIfEmpty()
            where restaurantIds.Contains(hold.RestaurantId)
                && dates.Contains(hold.ServiceDate)
                && !hold.IsDeleted
                && (hold.Status == CapacityHoldStatus.PaymentConfirmed
                    || hold.Status == CapacityHoldStatus.Confirmed
                    || (hold.Status == CapacityHoldStatus.PendingPayment && hold.ExpiresAtUtc > nowUtc))
                && (subscription == null || !GhostSubscriptionStatuses.Contains(subscription.Status))
            select new { hold.RestaurantId, hold.ServiceDate, hold.SubscriptionId, hold.Status, hold.Quantity })
            .ToListAsync(cancellationToken);

        var limitOverrides = await _db.DailyCapacities.AsNoTracking()
            .Where(dc => restaurantIds.Contains(dc.RestaurantId)
                && dates.Contains(dc.ServiceDate)
                && dc.CapacityType == CapacityType.DateOverride
                && dc.CampaignId == null
                && !dc.IsDeleted)
            .Select(dc => new { dc.RestaurantId, dc.ServiceDate, dc.MaxBoxes })
            .ToListAsync(cancellationToken);

        var baseRows = await _db.DailyCapacities.AsNoTracking()
            .Where(dc => restaurantIds.Contains(dc.RestaurantId)
                && dc.ServiceDate <= maxDate
                && dc.CapacityType == CapacityType.Base
                && dc.CampaignId == null
                && !dc.IsDeleted)
            .Select(dc => new { dc.RestaurantId, dc.ServiceDate, dc.MaxBoxes })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<(Guid, DateOnly), RestaurantCapacityUnits>(distinctKeys.Length);
        foreach (var key in distinctKeys)
        {
            var dayIdentitySet = dayIdentities
                .Where(x => x.RestaurantId == key.RestaurantId && x.ServiceDate == key.ServiceDate)
                .Select(x => x.SubscriptionId)
                .Distinct()
                .ToHashSet();

            var keyHolds = holdRows
                .Where(x => x.RestaurantId == key.RestaurantId && x.ServiceDate == key.ServiceDate)
                .ToList();
            var holdIdentitySet = keyHolds.Select(x => x.SubscriptionId).Distinct().ToHashSet();

            var totalCommitted = dayIdentitySet.Union(holdIdentitySet).Count();
            var dayOnly = dayIdentitySet.Except(holdIdentitySet).Count();
            var holdOnly = holdIdentitySet.Except(dayIdentitySet).Count();

            var limit = limitOverrides
                .Where(x => x.RestaurantId == key.RestaurantId && x.ServiceDate == key.ServiceDate)
                .Select(x => (int?)x.MaxBoxes)
                .FirstOrDefault()
                ?? baseRows
                    .Where(x => x.RestaurantId == key.RestaurantId && x.ServiceDate <= key.ServiceDate)
                    .OrderByDescending(x => x.ServiceDate)
                    .Select(x => (int?)x.MaxBoxes)
                    .FirstOrDefault();

            result[key] = new RestaurantCapacityUnits(
                key.RestaurantId,
                key.ServiceDate,
                limit,
                totalCommitted,
                dayIdentitySet.Count,
                holdIdentitySet.Count,
                dayOnly,
                holdOnly,
                keyHolds.Where(x => x.Status == CapacityHoldStatus.PendingPayment).Sum(x => x.Quantity),
                keyHolds.Where(x => x.Status == CapacityHoldStatus.PaymentConfirmed).Sum(x => x.Quantity),
                keyHolds.Where(x => x.Status == CapacityHoldStatus.Confirmed).Sum(x => x.Quantity));
        }

        return result;
    }
}
