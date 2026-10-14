using System.Data;
using MealMate.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Persistence.Common;

/// <summary>
/// PostgreSQL implementation of IApplicationLockManager using row-level FOR UPDATE locks
/// in deterministic order.
/// </summary>
public sealed class PostgresApplicationLockManager : IApplicationLockManager
{
    private readonly AppDbContext _db;

    private static readonly IComparer<Guid> GuidByteComparer = Comparer<Guid>.Create((a, b) =>
    {
        Span<byte> aBytes = stackalloc byte[16];
        Span<byte> bBytes = stackalloc byte[16];
        a.TryWriteBytes(aBytes);
        b.TryWriteBytes(bBytes);
        return aBytes.SequenceCompareTo(bBytes);
    });

    public PostgresApplicationLockManager(AppDbContext db)
    {
        _db = db;
    }

    public async Task LockSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsNpgsql()) return;
        var key = "subscription:" + subscriptionId.ToString("N");
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM \"Subscriptions\" WHERE \"Id\" = {subscriptionId} FOR UPDATE", cancellationToken);
        foreach (var entry in _db.ChangeTracker.Entries().Where(x =>
            x.Entity is MealMate.Domain.ProgramsSubscriptions.Subscription s && s.Id == subscriptionId ||
            x.Entity is MealMate.Domain.CalendarMeals.SubscriptionDay d && d.SubscriptionId == subscriptionId).ToList())
            await entry.ReloadAsync(cancellationToken);
    }

    public async Task LockFamilyBatchAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsNpgsql()) return;
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"FamilyCheckoutBatches\" WHERE \"Id\" = {batchId} FOR UPDATE", cancellationToken);
        foreach (var entry in _db.ChangeTracker.Entries().Where(x => x.Entity is MealMate.Domain.ProgramsSubscriptions.FamilyCheckoutBatch b && b.Id == batchId).ToList())
            await entry.ReloadAsync(cancellationToken);
    }

    public async Task LockSubscriptionDaysAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsNpgsql()) return;
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"SubscriptionDays\" WHERE \"SubscriptionId\" = {subscriptionId} ORDER BY \"Id\" FOR UPDATE", cancellationToken);
        foreach (var entry in _db.ChangeTracker.Entries().Where(x => x.Entity is MealMate.Domain.CalendarMeals.SubscriptionDay d && d.SubscriptionId == subscriptionId).ToList())
            await entry.ReloadAsync(cancellationToken);
    }

    public async Task LockCustomerWalletAsync(Guid payerCustomerUserId, CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsNpgsql()) return;
        var key = "customer-wallet:" + payerCustomerUserId.ToString("N");
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"CustomerCashWallets\" WHERE \"CustomerUserId\" = {payerCustomerUserId} FOR UPDATE", cancellationToken);
        foreach (var entry in _db.ChangeTracker.Entries().Where(x => x.Entity is MealMate.Domain.CustomerFinance.CustomerCashWallet w && w.CustomerUserId == payerCustomerUserId).ToList())
            await entry.ReloadAsync(cancellationToken);
    }

    public async Task LockFamilyGroupAsync(Guid familyGroupId, CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsNpgsql()) return;
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM \"FamilyGroups\" WHERE \"Id\" = {familyGroupId} FOR UPDATE", cancellationToken);
        foreach (var entry in _db.ChangeTracker.Entries().Where(x =>
            x.Entity is MealMate.Domain.ProgramsSubscriptions.FamilyGroup g && g.Id == familyGroupId ||
            x.Entity is MealMate.Domain.ProgramsSubscriptions.FamilyMember m && m.FamilyGroupId == familyGroupId ||
            x.Entity is MealMate.Domain.ProgramsSubscriptions.FamilyCheckoutBatch b && b.FamilyGroupId == familyGroupId).ToList())
            await entry.ReloadAsync(cancellationToken);
    }

    public async Task LockRefundInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsNpgsql()) return;
        var key = "payzah-refund:" + invoiceId;
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }

    public async Task LockPickupBoxAsync(Guid subscriptionDayId, CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsNpgsql()) return;

        // Row-level lock on TripStops matching this subscription day
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM \"TripStops\" WHERE \"SubscriptionDayId\" = {subscriptionDayId} FOR UPDATE",
            cancellationToken);

        // Row-level lock on SubscriptionDays
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM \"SubscriptionDays\" WHERE \"Id\" = {subscriptionDayId} FOR UPDATE",
            cancellationToken);
    }

    public Task LockRestaurantCapacityAsync(Guid restaurantId, DateOnly serviceDate, CancellationToken cancellationToken = default)
    {
        return LockRestaurantCapacityAsync(new[] { (restaurantId, serviceDate) }, cancellationToken);
    }

    public async Task LockRestaurantCapacityAsync(IEnumerable<(Guid RestaurantId, DateOnly ServiceDate)> keys, CancellationToken cancellationToken = default)
    {
        var orderedKeys = keys
            .Distinct()
            .OrderBy(k => k.RestaurantId, GuidByteComparer)
            .ThenBy(k => k.ServiceDate)
            .ToList();

        if (orderedKeys.Count == 0 || !_db.Database.IsNpgsql()) return;

        foreach (var (restaurantId, serviceDate) in orderedKeys)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"DailyCapacities\" WHERE \"RestaurantId\" = {restaurantId} AND \"ServiceDate\" = {serviceDate} FOR UPDATE",
                cancellationToken);
        }
    }

    public async Task LockAdAuctionAsync(Guid auctionId, CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsNpgsql()) return;
        var key = "ad-auction:" + auctionId.ToString("N");
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"AdAuctions\" WHERE \"Id\" = {auctionId} FOR UPDATE", cancellationToken);
        var tracked = _db.ChangeTracker.Entries<MealMate.Domain.Advertising.AdAuction>()
            .FirstOrDefault(x => x.Entity.Id == auctionId);
        if (tracked is not null)
        {
            await tracked.ReloadAsync(cancellationToken);
            foreach (var bidEntry in _db.ChangeTracker.Entries<MealMate.Domain.Advertising.AdBid>()
                         .Where(x => x.Entity.AdAuctionId == auctionId).ToList())
                bidEntry.State = EntityState.Detached;
            tracked.Entity.Bids.Clear();
            var bids = tracked.Collection(x => x.Bids);
            bids.IsLoaded = false;
            await bids.LoadAsync(cancellationToken);
        }
    }

    public async Task LockPromoCodeAsync(Guid promoCodeId, CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsNpgsql()) return;
        var key = "promo-code:" + promoCodeId.ToString("N");
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"PromoCodes\" WHERE \"Id\" = {promoCodeId} FOR UPDATE", cancellationToken);
        var tracked = _db.ChangeTracker.Entries<MealMate.Domain.ProgramsSubscriptions.PromoCode>()
            .FirstOrDefault(x => x.Entity.Id == promoCodeId);
        if (tracked is not null) await tracked.ReloadAsync(cancellationToken);
    }

    public async Task LockCampaignPaymentKeyAsync(Guid customerId, string key, CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsNpgsql()) return;
        var lockKey = $"campaign-payment-key:{customerId:N}:{key}";
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
    }

    public async Task LockCampaignCheckoutSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsNpgsql()) return;
        var campaignId = await _db.CampaignCheckoutSessions.AsNoTracking()
            .Where(s => s.Id == sessionId).Select(s => (Guid?)s.CampaignId).SingleOrDefaultAsync(cancellationToken);
        if (!campaignId.HasValue) return;
        // Every new payment/expiry mutation uses this campaign lock before the session row.
        // It protects shared restaurant capacity and subscriber counters as well as the individual payment.
        var lockKey = "campaign-checkout:" + campaignId.Value.ToString("N");
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"CampaignCheckoutSessions\" WHERE \"Id\" = {sessionId} FOR UPDATE", cancellationToken);
        foreach (var entry in _db.ChangeTracker.Entries().Where(e =>
                     e.Entity is MealMate.Domain.CollaborativeCampaigns.CampaignCheckoutSession s && s.Id == sessionId ||
                     e.Entity is MealMate.Domain.CollaborativeCampaigns.CampaignRestaurantCapacity c && c.CampaignId == campaignId ||
                     e.Entity is MealMate.Domain.CollaborativeCampaigns.CampaignSubscriberCap cap && cap.CampaignId == campaignId).ToList())
            await entry.ReloadAsync(cancellationToken);
    }
}
