using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MealMate.Persistence.CollaborativeCampaigns;

public sealed class CampaignCapacityLockRepository : ICampaignCapacityLockRepository
{
    private readonly AppDbContext _db;

    public CampaignCapacityLockRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Campaign?> LockCampaignAsync(Guid campaignId, CancellationToken ct = default)
    {
        if (_db.Database.IsNpgsql())
        {
            var key = $"campaign_lock:{campaignId:N}";
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }

        return await _db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId, ct);
    }

    public async Task<IReadOnlyList<CampaignRestaurantCapacity>> LockDailyCapacitiesAsync(
        Guid campaignId,
        IReadOnlyList<(DateOnly ServiceDate, Guid RestaurantId)> targets,
        CancellationToken ct = default)
    {
        if (targets.Count == 0)
        {
            return Array.Empty<CampaignRestaurantCapacity>();
        }

        var orderedTargets = targets
            .OrderBy(t => t.ServiceDate)
            .ThenBy(t => t.RestaurantId)
            .ToList();

        if (_db.Database.IsNpgsql())
        {
            foreach (var target in orderedTargets)
            {
                var key = $"campaign_cap:{campaignId:N}:{target.RestaurantId:N}:{target.ServiceDate:yyyy-MM-dd}";
                await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
            }
        }

        var dates = orderedTargets.Select(t => (DateOnly?)t.ServiceDate).Distinct().ToList();
        var restaurantIds = orderedTargets.Select(t => t.RestaurantId).Distinct().ToList();

        var capacities = await _db.CampaignRestaurantCapacities
            .Where(c => c.CampaignId == campaignId && dates.Contains(c.ServiceDate) && restaurantIds.Contains(c.RestaurantId))
            .OrderBy(c => c.ServiceDate)
            .ThenBy(c => c.RestaurantId)
            .ToListAsync(ct);

        return capacities;
    }

    public async Task<T> ExecuteInTransactionWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default)
    {
        if (!_db.Database.IsNpgsql())
        {
            return await operation(ct);
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            const int maxRetries = 3;
            var delay = TimeSpan.FromMilliseconds(50);

            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync(ct);
                    var result = await operation(ct);
                    await transaction.CommitAsync(ct);
                    return result;
                }
                catch (DbUpdateException ex) when (IsTransientPostgresException(ex) && attempt < maxRetries)
                {
                    await Task.Delay(delay, ct);
                    delay *= 2;
                }
                catch (PostgresException ex) when (IsTransientPostgresCode(ex.SqlState) && attempt < maxRetries)
                {
                    await Task.Delay(delay, ct);
                    delay *= 2;
                }
            }

            await using var finalTx = await _db.Database.BeginTransactionAsync(ct);
            var finalResult = await operation(ct);
            await finalTx.CommitAsync(ct);
            return finalResult;
        });
    }

    private static bool IsTransientPostgresException(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException pgEx)
        {
            return IsTransientPostgresCode(pgEx.SqlState);
        }
        return false;
    }

    private static bool IsTransientPostgresCode(string? sqlState)
    {
        // 23505: a concurrent duplicate insert (e.g. the same idempotency key committed by a
        // parallel twin transaction) — retry re-reads the committed winner and replays it.
        return sqlState is "40P01" or "40001" or "23505";
    }
}
