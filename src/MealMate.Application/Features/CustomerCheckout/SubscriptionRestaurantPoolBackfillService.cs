using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCheckout;

public sealed record RestaurantPoolBackfillResult(int Completed, int Unresolved);

public interface ISubscriptionRestaurantPoolBackfillService
{
    Task<RestaurantPoolBackfillResult> RunAsync(int batchSize = 100, CancellationToken cancellationToken = default);
}

public sealed class SubscriptionRestaurantPoolBackfillService : ISubscriptionRestaurantPoolBackfillService
{
    private readonly IAppDbContext _db;
    private readonly ICheckoutTransactionExecutor _transactions;
    private readonly IDateTimeProvider _clock;

    public SubscriptionRestaurantPoolBackfillService(IAppDbContext db, ICheckoutTransactionExecutor transactions, IDateTimeProvider clock)
        => (_db, _transactions, _clock) = (db, transactions, clock);

    public async Task<RestaurantPoolBackfillResult> RunAsync(int batchSize = 100, CancellationToken cancellationToken = default)
    {
        var candidateIds = await _db.Subscriptions.AsNoTracking()
            .Where(x => (x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.Scheduled
                    || x.Status == SubscriptionStatus.Frozen || x.Status == SubscriptionStatus.Completed)
                && !_db.SubscriptionRestaurantPoolSnapshots.Any(p => p.SubscriptionId == x.Id))
            .OrderBy(x => x.CreatedAtUtc)
            .Take(Math.Clamp(batchSize, 1, 500))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var completed = 0;
        var unresolved = 0;
        foreach (var id in candidateIds)
        {
            var success = await _transactions.ExecuteAsync(ct => BackfillOneAsync(id, ct), cancellationToken);
            if (success) completed++; else unresolved++;
        }
        return new(completed, unresolved);
    }

    private async Task<bool> BackfillOneAsync(Guid subscriptionId, CancellationToken ct)
    {
        if (await _db.SubscriptionRestaurantPoolSnapshots.AsNoTracking().AnyAsync(x => x.SubscriptionId == subscriptionId, ct))
            return true;
        var subscription = await _db.Subscriptions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == subscriptionId, ct);
        if (subscription is null) return false;
        var days = await _db.SubscriptionDays.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId && x.RestaurantId != null && x.ResolvedMenuPlanVersionId != null)
            .OrderBy(x => x.ServiceDate).ToListAsync(ct);
        var holds = await _db.CapacityHolds.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId && x.Status == CapacityHoldStatus.Confirmed)
            .OrderBy(x => x.ServiceDate).ToListAsync(ct);
        var historical = days.Select(x => new HistoricalDay(x.RestaurantId!.Value, x.ServiceDate, x.ResolvedMenuPlanVersionId!.Value))
            .Concat(holds.Select(x => new HistoricalDay(x.RestaurantId, x.ServiceDate, x.MenuPlanVersionId)))
            .Distinct()
            .OrderBy(x => x.ServiceDate)
            .ToList();
        if (historical.Count == 0)
            return await RecordIssueAsync(subscriptionId, "backfill.historical_restaurant_missing", new { subscription.Status }, ct);

        var finalSnapshots = await _db.SubscriptionDayMealSnapshots.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId).ToListAsync(ct);
        var unresolvedDays = historical.Where(x => !finalSnapshots.Any(s => s.RestaurantId == x.RestaurantId && s.ServiceDate == x.ServiceDate && s.MenuPlanVersionId == x.MenuPlanVersionId)).ToList();
        if (unresolvedDays.Count > 0)
            return await RecordIssueAsync(subscriptionId, "backfill.meal_snapshot_missing", new { unresolvedDays }, ct);

        var restaurantIds = historical.Select(x => x.RestaurantId).Distinct().ToList();
        var profiles = await _db.RestaurantProfiles.AsNoTracking().Where(x => restaurantIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var pool = new SubscriptionRestaurantPoolSnapshot
        {
            SubscriptionId = subscriptionId,
            EligibilityCheckedAtUtc = subscription.CreatedAtUtc == default ? _clock.UtcNow : subscription.CreatedAtUtc,
            RestaurantCount = restaurantIds.Count,
            IsBackfilled = true
        };
        _db.SubscriptionRestaurantPoolSnapshots.Add(pool);
        var initiallySelected = historical.First().RestaurantId;
        foreach (var restaurantId in restaurantIds)
        {
            profiles.TryGetValue(restaurantId, out var profile);
            var restaurant = new SubscriptionRestaurantSnapshot
            {
                PoolSnapshotId = pool.Id,
                SubscriptionId = subscriptionId,
                RestaurantId = restaurantId,
                ProgramId = subscription.ProgramId,
                BundleId = subscription.BundleId,
                RegionId = subscription.RegionId ?? Guid.Empty,
                Tier = subscription.SelectedTier,
                NameAr = profile?.TradeNameAr ?? "مطعم تاريخي",
                NameEn = profile?.TradeNameEn ?? "Historical restaurant",
                IsInitiallySelected = restaurantId == initiallySelected,
                OfferingSnapshotJson = JsonSerializer.Serialize(new { Type = "historical_backfill", subscription.ProgramId, subscription.BundleId }),
                CapturedAtUtc = _clock.UtcNow
            };
            _db.SubscriptionRestaurantSnapshots.Add(restaurant);
            foreach (var item in historical.Where(x => x.RestaurantId == restaurantId))
            {
                var source = finalSnapshots.Single(x => x.RestaurantId == item.RestaurantId && x.ServiceDate == item.ServiceDate && x.MenuPlanVersionId == item.MenuPlanVersionId);
                var hashInput = source.RequiredSlotsJson + "\n" + source.MealsJson;
                _db.SubscriptionRestaurantDaySnapshots.Add(new SubscriptionRestaurantDaySnapshot
                {
                    RestaurantSnapshotId = restaurant.Id,
                    SubscriptionId = subscriptionId,
                    RestaurantId = restaurantId,
                    ServiceDate = item.ServiceDate,
                    MenuPlanVersionId = item.MenuPlanVersionId,
                    SlotRequirementsJson = source.RequiredSlotsJson,
                    MealsJson = source.MealsJson,
                    ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput))).ToLowerInvariant(),
                    CapturedAtUtc = _clock.UtcNow
                });
            }
        }
        var previousIssue = await _db.SubscriptionSnapshotMigrationIssues.SingleOrDefaultAsync(x => x.SubscriptionId == subscriptionId, ct);
        if (previousIssue is not null) previousIssue.ResolvedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> RecordIssueAsync(Guid subscriptionId, string reasonCode, object details, CancellationToken ct)
    {
        var issue = await _db.SubscriptionSnapshotMigrationIssues.SingleOrDefaultAsync(x => x.SubscriptionId == subscriptionId, ct);
        if (issue is null)
        {
            issue = new SubscriptionSnapshotMigrationIssue { SubscriptionId = subscriptionId };
            _db.SubscriptionSnapshotMigrationIssues.Add(issue);
        }
        issue.ReasonCode = reasonCode;
        issue.DetailsJson = JsonSerializer.Serialize(details);
        issue.RecordedAtUtc = _clock.UtcNow;
        issue.ResolvedAtUtc = null;
        await _db.SaveChangesAsync(ct);
        return false;
    }

    private sealed record HistoricalDay(Guid RestaurantId, DateOnly ServiceDate, Guid MenuPlanVersionId);
}
