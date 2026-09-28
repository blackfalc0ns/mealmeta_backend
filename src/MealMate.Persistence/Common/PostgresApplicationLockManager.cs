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
