using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.RestaurantOperations.Readiness;

public sealed class RestaurantReadinessService : IRestaurantReadinessService
{
    private readonly IAppDbContext _db;
    private readonly IRestaurantEligibilityService _eligibility;
    private readonly ISubscriptionMealSnapshotService _snapshots;

    public RestaurantReadinessService(
        IAppDbContext db,
        IRestaurantEligibilityService eligibility,
        ISubscriptionMealSnapshotService snapshots)
    {
        _db = db;
        _eligibility = eligibility;
        _snapshots = snapshots;
    }

    public async Task<Result<RestaurantEligibilityDto>> GetMyReadinessAsync(
        Guid userId,
        DateOnly? from,
        int days = 7,
        Guid? programId = null,
        Guid? bundleId = null,
        RestaurantTier? tier = null,
        Guid? regionId = null,
        CancellationToken ct = default)
    {
        var restaurantId = await _db.RestaurantProfiles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);

        if (restaurantId is null)
            return Error.Forbidden("auth.forbidden", "A restaurant account is required.");

        var context = await ResolveContextAsync(restaurantId.Value, programId, bundleId, tier, regionId, ct);
        if (context is null)
            return Error.Validation("readiness.context_missing", "Activate an offering, tier classification and service region before checking readiness.");

        var rows = await _eligibility.EvaluateAsync(
            new RestaurantEligibilityRequest(
                context.Value.ProgramId,
                context.Value.BundleId,
                context.Value.Tier,
                context.Value.RegionId,
                from ?? DateOnly.FromDateTime(DateTime.UtcNow.Date),
                Math.Clamp(days, 1, 90),
                false,
                RestaurantId: restaurantId),
            ct);

        var row = rows.SingleOrDefault()
            ?? new RestaurantEligibilityDto(
                restaurantId.Value,
                string.Empty,
                string.Empty,
                false,
                [EligibilityReasonCodes.OfferingMissing],
                []);

        return Result.Success(row);
    }

    public async Task<Result<IReadOnlyList<RestaurantEligibilityDto>>> ListAdminReadinessAsync(
        Guid programId,
        Guid bundleId,
        RestaurantTier tier,
        Guid regionId,
        DateOnly? from,
        int days = 7,
        CancellationToken ct = default)
    {
        var rows = await _eligibility.EvaluateAsync(
            new RestaurantEligibilityRequest(
                programId,
                bundleId,
                tier,
                regionId,
                from ?? DateOnly.FromDateTime(DateTime.UtcNow.Date),
                Math.Clamp(days, 1, 90),
                false),
            ct);

        return Result.Success(rows);
    }

    public async Task<Result<RestaurantEligibilityDto?>> GetAdminRestaurantReadinessAsync(
        Guid restaurantId,
        Guid programId,
        Guid bundleId,
        RestaurantTier tier,
        Guid regionId,
        DateOnly? from,
        int days = 7,
        CancellationToken ct = default)
    {
        var rows = await _eligibility.EvaluateAsync(
            new RestaurantEligibilityRequest(
                programId,
                bundleId,
                tier,
                regionId,
                from ?? DateOnly.FromDateTime(DateTime.UtcNow.Date),
                Math.Clamp(days, 1, 90),
                false,
                RestaurantId: restaurantId),
            ct);

        return Result.Success<RestaurantEligibilityDto?>(rows.SingleOrDefault());
    }

    public async Task<Result<MealSnapshotBackfillResultDto>> BackfillSnapshotsAsync(CancellationToken ct = default)
    {
        var ids = await _db.Subscriptions.AsNoTracking()
            .Where(x => x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.Scheduled)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var created = 0;
        foreach (var id in ids)
            created += await _snapshots.CreateMissingAsync(id, ct);

        var unresolved = await (
            from day in _db.SubscriptionDays.AsNoTracking()
            join subscription in _db.Subscriptions.AsNoTracking() on day.SubscriptionId equals subscription.Id
            where (subscription.Status == SubscriptionStatus.Active || subscription.Status == SubscriptionStatus.Scheduled)
                && !_db.SubscriptionDayMealSnapshots.Any(x => x.SubscriptionDayId == day.Id)
            select new UnresolvedSnapshotDayDto(
                day.SubscriptionId,
                day.Id,
                day.ServiceDate,
                "snapshot_source_unresolvable")).ToListAsync(ct);

        var result = new MealSnapshotBackfillResultDto(ids.Count, created, unresolved);
        return Result.Success(result);
    }

    private async Task<(Guid ProgramId, Guid BundleId, RestaurantTier Tier, Guid RegionId)?> ResolveContextAsync(
        Guid restaurantId,
        Guid? programId,
        Guid? bundleId,
        RestaurantTier? tier,
        Guid? regionId,
        CancellationToken ct)
    {
        var offer = programId.HasValue && bundleId.HasValue
            ? new { ProgramId = programId.Value, BundleId = bundleId.Value }
            : await _db.RestaurantProgramBundleOffers.AsNoTracking()
                .Where(x => x.RestaurantId == restaurantId && x.IsActive)
                .Select(x => new { x.ProgramId, x.BundleId })
                .FirstOrDefaultAsync(ct);

        if (offer is null) return null;

        var resolvedTier = tier ?? await (
            from c in _db.RestaurantClassifications.AsNoTracking()
            join run in _db.ClassificationRuns.AsNoTracking() on c.ClassificationRunId equals run.Id
            where c.RestaurantId == restaurantId && c.ProgramId == offer.ProgramId && c.BundleId == offer.BundleId
            orderby run.RunAtUtc descending
            select (RestaurantTier?)c.Tier).FirstOrDefaultAsync(ct);

        var resolvedRegion = regionId ?? await _db.RestaurantServiceRegions.AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId && x.IsActive)
            .Select(x => (Guid?)x.RegionId)
            .FirstOrDefaultAsync(ct);

        return resolvedTier.HasValue && resolvedRegion.HasValue
            ? (offer.ProgramId, offer.BundleId, resolvedTier.Value, resolvedRegion.Value)
            : null;
    }
}
