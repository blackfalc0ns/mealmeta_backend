using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/admin/checkout")]
public sealed class AdminCheckoutOperationsController : ApiControllerBase
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    public AdminCheckoutOperationsController(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock) => (_db, _currentUser, _clock) = (db, currentUser, clock);

    [HttpGet("capacity-holds")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> Holds([FromQuery] CapacityHoldStatus? status, CancellationToken cancellationToken)
    {
        var query = _db.CapacityHolds.AsNoTracking().AsQueryable();
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        return Ok(await query.OrderBy(x => x.ExpiresAtUtc).Take(500).Select(x => new { x.Id, x.SubscriptionId, x.RestaurantId, x.ServiceDate, x.Quantity, x.Status, x.ExpiresAtUtc, x.ConfirmedAtUtc, x.ReleasedAtUtc }).ToListAsync(cancellationToken));
    }

    [HttpPost("capacity-holds/expire")]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> ExpireHolds(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var holds = await _db.CapacityHolds.Where(x => x.Status == CapacityHoldStatus.PendingPayment && x.ExpiresAtUtc <= now).ToListAsync(cancellationToken);
        foreach (var hold in holds) hold.Status = CapacityHoldStatus.Expired;
        if (holds.Count > 0) await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { expired = holds.Count });
    }

    [HttpPost("/api/v1/admin/subscriptions/{id:guid}/days/{date}/reassign-restaurant")]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> Reassign(Guid id, DateOnly date, [FromBody] ReassignRestaurantRequest request, CancellationToken cancellationToken)
    {
        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        var day = await _db.SubscriptionDays.FirstOrDefaultAsync(x => x.SubscriptionId == id && x.ServiceDate == date, cancellationToken);
        if (subscription is null || day is null || day.Status is SubscriptionDayStatus.Cancelled or SubscriptionDayStatus.Delivered) return BadRequest(new { error = "reassignment.day_unavailable" });
        var menu = await (from v in _db.MenuPlanVersions
                          join p in _db.MenuPlans on v.MenuPlanId equals p.Id
                          join pp in _db.MenuPlanPrograms on p.Id equals pp.MenuPlanId
                          where p.RestaurantId == request.RestaurantId && pp.ProgramId == subscription.ProgramId && v.Status == VerificationStatus.Approved && !v.IsPaused && v.EffectiveFrom <= date
                          orderby v.EffectiveFrom descending select v).FirstOrDefaultAsync(cancellationToken);
        var eligible = menu is not null && await _db.RestaurantProfiles.AnyAsync(x => x.Id == request.RestaurantId && x.VerificationStatus == VerificationStatus.Approved && x.IsOperationallyActive, cancellationToken)
            && await _db.RestaurantProgramBundleOffers.AnyAsync(x => x.RestaurantId == request.RestaurantId && x.ProgramId == subscription.ProgramId && x.BundleId == subscription.BundleId && x.IsActive, cancellationToken)
            && (!subscription.RegionId.HasValue || await _db.RestaurantServiceRegions.AnyAsync(x => x.RestaurantId == request.RestaurantId && x.RegionId == subscription.RegionId && x.IsActive, cancellationToken));
        if (!eligible) return BadRequest(new { error = "reassignment.restaurant_not_eligible" });
        var limit = await _db.DailyCapacities.Where(x => x.RestaurantId == request.RestaurantId && x.ServiceDate == date && x.CapacityType == CapacityType.DateOverride).Select(x => (int?)x.MaxBoxes).FirstOrDefaultAsync(cancellationToken) ?? 40;
        var occupied = await _db.SubscriptionDays.CountAsync(x => x.RestaurantId == request.RestaurantId && x.ServiceDate == date && x.Status != SubscriptionDayStatus.Cancelled, cancellationToken);
        if (occupied >= limit) return BadRequest(new { error = "reassignment.capacity_unavailable" });
        day.RestaurantId = request.RestaurantId;
        day.ResolvedMenuPlanVersionId = menu!.Id;
        day.Status = SubscriptionDayStatus.ReplacementWindowOpen;
        var hold = await _db.CapacityHolds.FirstOrDefaultAsync(x => x.SubscriptionId == id && x.ServiceDate == date, cancellationToken);
        if (hold is not null) { hold.RestaurantId = request.RestaurantId; hold.MenuPlanVersionId = menu.Id; hold.Status = CapacityHoldStatus.Confirmed; }
        _db.SubscriptionOperations.Add(new SubscriptionOperation { SubscriptionId = id, OperationType = "DAY_REASSIGNED", PreviousStatus = subscription.Status, NewStatus = subscription.Status, Reason = request.Reason, PerformedByUserId = _currentUser.UserId ?? Guid.Empty, PerformedByUserType = _currentUser.UserType ?? UserType.Admin, TimestampUtc = _clock.UtcNow });
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { day.Id, day.RestaurantId, day.ResolvedMenuPlanVersionId, day.Status });
    }
}

public sealed record ReassignRestaurantRequest(Guid RestaurantId, string? Reason = null);
