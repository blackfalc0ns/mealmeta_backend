using MealMate.Api.Common;
using MealMate.Api.Authorization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[Route("api/v1/restaurants")]
public sealed class RestaurantReadinessController : ApiControllerBase
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IRestaurantEligibilityService _eligibility;

    public RestaurantReadinessController(IAppDbContext db, ICurrentUser currentUser, IRestaurantEligibilityService eligibility)
        => (_db, _currentUser, _eligibility) = (db, currentUser, eligibility);

    [HttpGet("readiness")]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> Get([FromQuery] DateOnly? from, [FromQuery] int days = 7, [FromQuery] Guid? programId = null, [FromQuery] Guid? bundleId = null, [FromQuery] RestaurantTier? tier = null, [FromQuery] Guid? regionId = null, CancellationToken ct = default)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();
        var restaurantId = await _db.RestaurantProfiles.AsNoTracking().Where(x => x.UserId == userId).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (restaurantId is null) return Forbid();
        var context = await ResolveContext(restaurantId.Value, programId, bundleId, tier, regionId, ct);
        if (context is null) return BadRequest(new { code = "readiness.context_missing", message = "Activate an offering, tier classification and service region before checking readiness." });
        var rows = await _eligibility.EvaluateAsync(new RestaurantEligibilityRequest(context.Value.ProgramId, context.Value.BundleId, context.Value.Tier, context.Value.RegionId, from ?? DateOnly.FromDateTime(DateTime.UtcNow.Date), Math.Clamp(days, 1, 90), false, RestaurantId: restaurantId), ct);
        var row = rows.SingleOrDefault();
        return row is null
            ? Ok(new { restaurantId, isEligible = false, reasonCodes = new[] { EligibilityReasonCodes.OfferingMissing } })
            : Ok(row);
    }

    private async Task<(Guid ProgramId, Guid BundleId, RestaurantTier Tier, Guid RegionId)?> ResolveContext(Guid restaurantId, Guid? programId, Guid? bundleId, RestaurantTier? tier, Guid? regionId, CancellationToken ct)
    {
        var offer = programId.HasValue && bundleId.HasValue ? new { ProgramId = programId.Value, BundleId = bundleId.Value } : await _db.RestaurantProgramBundleOffers.AsNoTracking().Where(x => x.RestaurantId == restaurantId && x.IsActive).Select(x => new { x.ProgramId, x.BundleId }).FirstOrDefaultAsync(ct);
        if (offer is null) return null;
        var resolvedTier = tier ?? await (from c in _db.RestaurantClassifications.AsNoTracking() join run in _db.ClassificationRuns.AsNoTracking() on c.ClassificationRunId equals run.Id where c.RestaurantId == restaurantId && c.ProgramId == offer.ProgramId && c.BundleId == offer.BundleId orderby run.RunAtUtc descending select (RestaurantTier?)c.Tier).FirstOrDefaultAsync(ct);
        var resolvedRegion = regionId ?? await _db.RestaurantServiceRegions.AsNoTracking().Where(x => x.RestaurantId == restaurantId && x.IsActive).Select(x => (Guid?)x.RegionId).FirstOrDefaultAsync(ct);
        return resolvedTier.HasValue && resolvedRegion.HasValue ? (offer.ProgramId, offer.BundleId, resolvedTier.Value, resolvedRegion.Value) : null;
    }
}

[Authorize]
[Route("api/v1/admin/restaurants")]
public sealed class AdminRestaurantReadinessController : ApiControllerBase
{
    private readonly IRestaurantEligibilityService _eligibility;
    private readonly ISubscriptionMealSnapshotService _snapshots;
    private readonly IAppDbContext _db;
    public AdminRestaurantReadinessController(IRestaurantEligibilityService eligibility, ISubscriptionMealSnapshotService snapshots, IAppDbContext db)
        => (_eligibility, _snapshots, _db) = (eligibility, snapshots, db);

    [HttpGet("readiness")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> List([FromQuery] Guid programId, [FromQuery] Guid bundleId, [FromQuery] RestaurantTier tier, [FromQuery] Guid regionId, [FromQuery] DateOnly? from, [FromQuery] int days = 7, CancellationToken ct = default)
        => Ok(await _eligibility.EvaluateAsync(new RestaurantEligibilityRequest(programId, bundleId, tier, regionId, from ?? DateOnly.FromDateTime(DateTime.UtcNow.Date), Math.Clamp(days, 1, 90), false), ct));

    [HttpGet("{id:guid}/readiness")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> One(Guid id, [FromQuery] Guid programId, [FromQuery] Guid bundleId, [FromQuery] RestaurantTier tier, [FromQuery] Guid regionId, [FromQuery] DateOnly? from, [FromQuery] int days = 7, CancellationToken ct = default)
        => Ok((await _eligibility.EvaluateAsync(new RestaurantEligibilityRequest(programId, bundleId, tier, regionId, from ?? DateOnly.FromDateTime(DateTime.UtcNow.Date), Math.Clamp(days, 1, 90), false, RestaurantId: id), ct)).SingleOrDefault());

    [HttpPost("/api/v1/admin/subscriptions/meal-snapshots/backfill")]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> BackfillSnapshots(CancellationToken ct)
    {
        var ids = await _db.Subscriptions.AsNoTracking().Where(x => x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.Scheduled).Select(x => x.Id).ToListAsync(ct);
        var created = 0;
        foreach (var id in ids) created += await _snapshots.CreateMissingAsync(id, ct);
        var unresolved = await (from day in _db.SubscriptionDays.AsNoTracking()
                                join subscription in _db.Subscriptions.AsNoTracking() on day.SubscriptionId equals subscription.Id
                                where (subscription.Status == SubscriptionStatus.Active || subscription.Status == SubscriptionStatus.Scheduled)
                                   && !_db.SubscriptionDayMealSnapshots.Any(x => x.SubscriptionDayId == day.Id)
                                select new { day.SubscriptionId, SubscriptionDayId = day.Id, day.ServiceDate, reasonCode = "snapshot_source_unresolvable" }).ToListAsync(ct);
        return Ok(new { subscriptionsScanned = ids.Count, snapshotsCreated = created, unresolved });
    }
}
