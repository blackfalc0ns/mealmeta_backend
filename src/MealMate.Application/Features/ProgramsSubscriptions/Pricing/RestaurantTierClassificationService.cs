using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.Pricing;

public sealed record RestaurantTierAssignment(
    Guid RestaurantId,
    RestaurantTier Tier,
    decimal ReferencePrice26,
    decimal ReferenceDailyBox,
    bool IsOutlierFlagged,
    string? OutlierReason);

public sealed record RestaurantTierClassificationResult(
    Guid ProgramId,
    Guid BundleId,
    decimal MeanReferenceDaily,
    decimal SdReferenceDaily,
    decimal LowerBound,
    decimal UpperBound,
    IReadOnlyList<RestaurantTierAssignment> Assignments);

public interface IRestaurantTierClassificationService
{
    Task<RestaurantTierClassificationResult> ResolveAsync(Guid programId, Guid bundleId, bool eligibleOnly, CancellationToken ct);
}

public sealed class RestaurantTierClassificationService : IRestaurantTierClassificationService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public RestaurantTierClassificationService(IAppDbContext db, IDateTimeProvider clock)
        => (_db, _clock) = (db, clock);

    public async Task<RestaurantTierClassificationResult> ResolveAsync(Guid programId, Guid bundleId, bool eligibleOnly, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        var query = _db.RestaurantBundlePricings
            .AsNoTracking()
            .Where(x => x.ProgramId == programId
                && x.BundleId == bundleId
                && x.EffectiveFromUtc <= now
                && (x.EffectiveToUtc == null || x.EffectiveToUtc > now)
                && (x.LockStatus == PriceLockStatus.Locked
                    || x.LockStatus == PriceLockStatus.Approved
                    || x.LockStatus == PriceLockStatus.ApprovedWithEffectiveDate)
                && x.ReferencePrice26.Amount > 0m);

        var rows = await query.ToListAsync(ct);

        if (eligibleOnly && rows.Count > 0)
        {
            var candidateIds = rows.Select(x => x.RestaurantId).Distinct().ToList();

            var offeredIds = await _db.RestaurantProgramBundleOffers
                .AsNoTracking()
                .Where(x => candidateIds.Contains(x.RestaurantId)
                    && x.ProgramId == programId
                    && x.BundleId == bundleId
                    && x.IsActive)
                .Select(x => x.RestaurantId)
                .Distinct()
                .ToListAsync(ct);

            var approvedIds = await _db.RestaurantProfiles
                .AsNoTracking()
                .Where(x => candidateIds.Contains(x.Id)
                    && x.VerificationStatus == VerificationStatus.Approved
                    && x.IsOperationallyActive)
                .Select(x => x.Id)
                .ToListAsync(ct);

            var eligibleIds = offeredIds.Intersect(approvedIds).ToHashSet();
            rows = rows.Where(x => eligibleIds.Contains(x.RestaurantId)).ToList();
        }

        // Keep the newest open row per restaurant.
        var assignments = rows
            .GroupBy(x => x.RestaurantId)
            .Select(g => g.OrderByDescending(x => x.EffectiveFromUtc).First())
            .Select(x => new { x.RestaurantId, x.ReferencePrice26.Amount })
            .ToList();

        if (assignments.Count == 0)
        {
            return new RestaurantTierClassificationResult(
                programId,
                bundleId,
                0m,
                0m,
                0m,
                0m,
                Array.Empty<RestaurantTierAssignment>());
        }

        var dailyPrices = assignments.Select(x => x.Amount / 26m).ToList();
        var mean = dailyPrices.Average();
        var variance = dailyPrices.Sum(value => (value - mean) * (value - mean)) / dailyPrices.Count;
        var standardDeviation = (decimal)Math.Sqrt((double)variance);
        var lowerBound = mean - (2m * standardDeviation);
        var upperBound = mean + (2m * standardDeviation);

        var resultAssignments = assignments
            .Select(x =>
            {
                var daily = x.Amount / 26m;
                var isOutlier = daily < lowerBound || daily > upperBound;
                return new RestaurantTierAssignment(
                    x.RestaurantId,
                    SubscriptionPackagePricingCalculator.ResolveTier(daily, assignments.Count, mean, standardDeviation),
                    x.Amount,
                    daily,
                    isOutlier,
                    isOutlier ? $"Daily reference price {daily:F3} KWD is outside 2-sigma bounds [{lowerBound:F3}, {upperBound:F3}]" : null);
            })
            .ToList();

        return new RestaurantTierClassificationResult(
            programId,
            bundleId,
            mean,
            standardDeviation,
            lowerBound,
            upperBound,
            resultAssignments);
    }
}
