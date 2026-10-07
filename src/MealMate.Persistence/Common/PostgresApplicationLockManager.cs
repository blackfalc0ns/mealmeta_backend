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
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM \"Subscriptions\" WHERE \"Id\" = {subscriptionId} FOR UPDATE", cancellationToken);
        foreach (var entry in _db.ChangeTracker.Entries().Where(x =>
            x.Entity is MealMate.Domain.ProgramsSubscriptions.Subscription s && s.Id == subscriptionId ||
            x.Entity is MealMate.Domain.CalendarMeals.SubscriptionDay d && d.SubscriptionId == subscriptionId).ToList())
            await entry.ReloadAsync(cancellationToken);
    }

    public async Task LockFamilyGroupAsync(Guid familyGroupId, CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsNpgsql()) return;
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM \"FamilyGroups\" WHERE \"Id\" = {familyGroupId} FOR UPDATE", cancellationToken);
        foreach (var entry in _db.ChangeTracker.Entries().Where(x =>
            x.Entity is MealMate.Domain.ProgramsSubscriptions.FamilyGroup group && group.Id == familyGroupId).ToList())
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
}
