using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.CustomerCheckout;
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
    private readonly IRestaurantEligibilityService _eligibility;
    private readonly ICapacityConcurrencyLock _capacityLock;
    private readonly ICheckoutTransactionExecutor _transactions;
    private readonly ISubscriptionMealSnapshotService _snapshots;
    private readonly ICapacityHoldExpiryService _expiry;
    private readonly ISubscriptionFulfillmentService _fulfillment;
    public AdminCheckoutOperationsController(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock, IRestaurantEligibilityService eligibility, ICapacityConcurrencyLock capacityLock, ICheckoutTransactionExecutor transactions, ISubscriptionMealSnapshotService snapshots, ICapacityHoldExpiryService expiry, ISubscriptionFulfillmentService fulfillment)
        => (_db, _currentUser, _clock, _eligibility, _capacityLock, _transactions, _snapshots, _expiry, _fulfillment) = (db, currentUser, clock, eligibility, capacityLock, transactions, snapshots, expiry, fulfillment);

    [HttpGet("capacity-holds")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> Holds([FromQuery] CapacityHoldStatus? status, CancellationToken cancellationToken)
    {
        var query = _db.CapacityHolds.AsNoTracking().AsQueryable();
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        return Ok(await query.OrderBy(x => x.ExpiresAtUtc).Take(500).Select(x => new { x.Id, x.SubscriptionId, x.RestaurantId, x.ServiceDate, x.Quantity, x.Status, x.ExpiresAtUtc, x.ConfirmedAtUtc, x.ReleasedAtUtc }).ToListAsync(cancellationToken));
    }

    [HttpGet("snapshot-migration-issues")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> SnapshotMigrationIssues([FromQuery] bool unresolvedOnly = true, CancellationToken cancellationToken = default)
    {
        var query = _db.SubscriptionSnapshotMigrationIssues.AsNoTracking();
        if (unresolvedOnly) query = query.Where(x => x.ResolvedAtUtc == null);
        return Ok(await query.OrderByDescending(x => x.RecordedAtUtc).Take(500)
            .Select(x => new { x.SubscriptionId, x.ReasonCode, x.DetailsJson, x.RecordedAtUtc, x.ResolvedAtUtc })
            .ToListAsync(cancellationToken));
    }

    [HttpPost("capacity-holds/expire")]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> ExpireHolds(CancellationToken cancellationToken)
    {
        var result = await _expiry.ExpireDueAsync(cancellationToken);
        return Ok(new { expired = result.HoldsExpired, subscriptionsExpired = result.SubscriptionsExpired });
    }

    [HttpPost("/api/v1/admin/subscriptions/{id:guid}/retry-fulfillment")]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> RetryFulfillment(Guid id, CancellationToken cancellationToken)
    {
        var result = await _fulfillment.TryFulfillAsync(id, cancellationToken);
        return result.IsSuccess
            ? Ok(new { subscriptionId = id, status = "Completed", result.IsAlreadyComplete })
            : Conflict(new { subscriptionId = id, status = "PendingRetry", error = result.ErrorCode });
    }

    [HttpPost("/api/v1/admin/subscriptions/{id:guid}/days/{date}/reassign-restaurant")]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> Reassign(Guid id, DateOnly date, [FromBody] ReassignRestaurantRequest request, CancellationToken cancellationToken)
    {
        var outcome = await _transactions.ExecuteAsync(async transactionCt =>
        {
            await _capacityLock.AcquireSubscriptionAsync(id, transactionCt);
            var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == id, transactionCt);
            var day = await _db.SubscriptionDays.FirstOrDefaultAsync(x => x.SubscriptionId == id && x.ServiceDate == date, transactionCt);
            if (subscription is null || day is null || day.Status is SubscriptionDayStatus.Cancelled or SubscriptionDayStatus.Delivered)
                return new ReassignmentOutcome("reassignment.day_unavailable");

            foreach (var restaurantId in new[] { day.RestaurantId, request.RestaurantId }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().OrderBy(x => x))
                await _capacityLock.AcquireAsync(restaurantId, [date], transactionCt);

            var options = await GetFrozenRestaurantOptionsAsync(id, date, day.RestaurantId, transactionCt);
            var candidate = options.SingleOrDefault(x => x.RestaurantId == request.RestaurantId);
            if (candidate is null || !candidate.IsEligible)
            {
                if (options.All(x => !x.IsEligible))
                {
                    day.Status = SubscriptionDayStatus.ReplacementRequired;
                    await _db.SaveChangesAsync(transactionCt);
                }
                return new ReassignmentOutcome(candidate is null ? "reassignment.restaurant_not_in_frozen_pool" : "reassignment.restaurant_not_eligible", candidate?.ReasonCodes);
            }

            var source = await _db.SubscriptionRestaurantDaySnapshots.AsNoTracking().SingleAsync(
                x => x.SubscriptionId == id && x.RestaurantId == request.RestaurantId && x.ServiceDate == date, transactionCt);
            var previousRestaurantId = day.RestaurantId;
            day.RestaurantId = request.RestaurantId;
            day.ResolvedMenuPlanVersionId = source.MenuPlanVersionId;
            day.Status = SubscriptionDayStatus.ReplacementWindowOpen;
            day.LockAtUtc = _clock.UtcNow;
            day.ConfirmedAtUtc = null;
            var hold = await _db.CapacityHolds.FirstOrDefaultAsync(x => x.SubscriptionId == id && x.ServiceDate == date, transactionCt);
            if (hold is not null) { hold.RestaurantId = request.RestaurantId; hold.MenuPlanVersionId = source.MenuPlanVersionId; hold.Status = CapacityHoldStatus.Confirmed; }
            var selections = await _db.DailyMealSelections.Where(x => x.SubscriptionDayId == day.Id).ToListAsync(transactionCt);
            _db.DailyMealSelections.RemoveRange(selections);
            var oldSnapshot = await _db.SubscriptionDayMealSnapshots.FirstOrDefaultAsync(x => x.SubscriptionDayId == day.Id, transactionCt);
            if (oldSnapshot is not null) _db.SubscriptionDayMealSnapshots.Remove(oldSnapshot);
            _db.SubscriptionDayMealSnapshots.Add(new MealMate.Domain.CalendarMeals.SubscriptionDayMealSnapshot
            {
                SubscriptionId = id,
                SubscriptionDayId = day.Id,
                ServiceDate = date,
                RestaurantId = request.RestaurantId,
                ProgramId = subscription.ProgramId,
                MenuPlanVersionId = source.MenuPlanVersionId,
                RequiredSlotsJson = source.SlotRequirementsJson,
                MealsJson = source.MealsJson,
                CapturedAtUtc = _clock.UtcNow
            });
            _db.SubscriptionOperations.Add(new SubscriptionOperation { SubscriptionId = id, OperationType = "DAY_REASSIGNED", PreviousStatus = subscription.Status, NewStatus = subscription.Status, Reason = $"OldRestaurantId={previousRestaurantId}; NewRestaurantId={request.RestaurantId}; {request.Reason}", PerformedByUserId = _currentUser.UserId ?? Guid.Empty, PerformedByUserType = _currentUser.UserType ?? UserType.Admin, TimestampUtc = _clock.UtcNow });
            await _db.SaveChangesAsync(transactionCt);
            return new ReassignmentOutcome(null, null, day.Id, request.RestaurantId, source.MenuPlanVersionId, day.Status);
        }, cancellationToken);
        return outcome.ErrorCode is null
            ? Ok(new { id = outcome.DayId, restaurantId = outcome.RestaurantId, resolvedMenuPlanVersionId = outcome.MenuPlanVersionId, status = outcome.Status })
            : BadRequest(new { error = outcome.ErrorCode, reasonCodes = outcome.ReasonCodes });
    }

    [HttpGet("/api/v1/admin/subscriptions/{id:guid}/days/{date}/restaurant-options")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    [ProducesResponseType(typeof(IReadOnlyList<FrozenRestaurantOption>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RestaurantOptions(Guid id, DateOnly date, CancellationToken cancellationToken)
    {
        var day = await _db.SubscriptionDays.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriptionId == id && x.ServiceDate == date, cancellationToken);
        if (day is null) return NotFound();
        return Ok(await GetFrozenRestaurantOptionsAsync(id, date, day.RestaurantId, cancellationToken));
    }

    private async Task<List<FrozenRestaurantOption>> GetFrozenRestaurantOptionsAsync(Guid subscriptionId, DateOnly date, Guid? currentRestaurantId, CancellationToken ct)
    {
        var frozen = await (from restaurant in _db.SubscriptionRestaurantSnapshots.AsNoTracking()
                            join day in _db.SubscriptionRestaurantDaySnapshots.AsNoTracking()
                                on new { restaurant.SubscriptionId, restaurant.RestaurantId } equals new { day.SubscriptionId, day.RestaurantId }
                            where restaurant.SubscriptionId == subscriptionId && day.ServiceDate == date && restaurant.RestaurantId != currentRestaurantId
                            select new { restaurant.RestaurantId, restaurant.NameAr, restaurant.NameEn, day.MenuPlanVersionId }).ToListAsync(ct);
        var result = new List<FrozenRestaurantOption>();
        foreach (var item in frozen)
        {
            var reasons = new List<string>();
            var profile = await _db.RestaurantProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == item.RestaurantId, ct);
            if (profile is null || profile.VerificationStatus != VerificationStatus.Approved || !profile.IsOperationallyActive)
                reasons.Add("restaurant_inactive");
            var exception = await _db.RestaurantOperatingDayExceptions.AsNoTracking().SingleOrDefaultAsync(x => x.RestaurantId == item.RestaurantId && x.Date == date, ct);
            var schedulesExist = await _db.RestaurantOperatingSchedules.AsNoTracking().AnyAsync(x => x.RestaurantId == item.RestaurantId, ct);
            var working = exception?.IsWorkingDay ?? (!schedulesExist || await _db.RestaurantOperatingSchedules.AsNoTracking().AnyAsync(x => x.RestaurantId == item.RestaurantId && x.DayOfWeek == date.DayOfWeek && x.IsWorkingDay, ct));
            if (!working) reasons.Add(EligibilityReasonCodes.RestaurantClosed);
            var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            if (await _db.BusyStatuses.AsNoTracking().AnyAsync(x => x.RestaurantId == item.RestaurantId && x.IsBusy && x.StartsAtUtc < dayStart.AddDays(1) && (x.EndsAtUtc == null || x.EndsAtUtc >= dayStart), ct))
                reasons.Add(EligibilityReasonCodes.RestaurantBusy);
            var limit = await _db.DailyCapacities.AsNoTracking().Where(x => x.RestaurantId == item.RestaurantId && x.ServiceDate == date && x.CapacityType == CapacityType.DateOverride).Select(x => (int?)x.MaxBoxes).FirstOrDefaultAsync(ct)
                ?? await _db.DailyCapacities.AsNoTracking().Where(x => x.RestaurantId == item.RestaurantId && x.CapacityType == CapacityType.Base && x.ServiceDate <= date).OrderByDescending(x => x.ServiceDate).Select(x => (int?)x.MaxBoxes).FirstOrDefaultAsync(ct);
            var confirmed = await _db.SubscriptionDays.AsNoTracking().CountAsync(x => x.RestaurantId == item.RestaurantId && x.ServiceDate == date && x.Status != SubscriptionDayStatus.Cancelled, ct);
            var reserved = await _db.CapacityHolds.AsNoTracking().Where(x => x.RestaurantId == item.RestaurantId && x.ServiceDate == date
                && ((x.Status == CapacityHoldStatus.PendingPayment && x.ExpiresAtUtc > _clock.UtcNow) || x.Status == CapacityHoldStatus.PaymentConfirmed))
                .SumAsync(x => (int?)x.Quantity, ct) ?? 0;
            if (limit is null || confirmed + reserved >= limit) reasons.Add(EligibilityReasonCodes.CapacityUnavailable);
            result.Add(new FrozenRestaurantOption(item.RestaurantId, item.NameAr, item.NameEn, item.MenuPlanVersionId, reasons.Count == 0, reasons));
        }
        return result;
    }
}

public sealed record ReassignRestaurantRequest(Guid RestaurantId, string? Reason = null);
public sealed record ReassignmentOutcome(string? ErrorCode, IReadOnlyList<string>? ReasonCodes = null, Guid? DayId = null, Guid? RestaurantId = null, Guid? MenuPlanVersionId = null, SubscriptionDayStatus? Status = null);
public sealed record FrozenRestaurantOption(Guid RestaurantId, string NameAr, string NameEn, Guid MenuPlanVersionId, bool IsEligible, IReadOnlyList<string> ReasonCodes);
