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
    public AdminCheckoutOperationsController(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock, IRestaurantEligibilityService eligibility, ICapacityConcurrencyLock capacityLock, ICheckoutTransactionExecutor transactions, ISubscriptionMealSnapshotService snapshots)
        => (_db, _currentUser, _clock, _eligibility, _capacityLock, _transactions, _snapshots) = (db, currentUser, clock, eligibility, capacityLock, transactions, snapshots);

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
        var subscriptionIds = holds.Select(x => x.SubscriptionId).Distinct().ToList();
        var subscriptions = await _db.Subscriptions.Where(x => subscriptionIds.Contains(x.Id) && x.Status == SubscriptionStatus.PendingPayment).ToListAsync(cancellationToken);
        foreach (var subscription in subscriptions)
        {
            subscription.Status = SubscriptionStatus.Expired;
            subscription.LastOperationAtUtc = now;
            _db.SubscriptionOperations.Add(new SubscriptionOperation
            {
                SubscriptionId = subscription.Id, OperationType = "HOLD_EXPIRED", PreviousStatus = SubscriptionStatus.PendingPayment,
                NewStatus = SubscriptionStatus.Expired, Reason = "Expired by checkout operations endpoint.",
                PerformedByUserId = _currentUser.UserId ?? Guid.Empty, PerformedByUserType = _currentUser.UserType ?? UserType.Admin, TimestampUtc = now
            });
        }
        if (holds.Count > 0) await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { expired = holds.Count, subscriptionsExpired = subscriptions.Count });
    }

    [HttpPost("/api/v1/admin/subscriptions/{id:guid}/days/{date}/reassign-restaurant")]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> Reassign(Guid id, DateOnly date, [FromBody] ReassignRestaurantRequest request, CancellationToken cancellationToken)
    {
        var outcome = await _transactions.ExecuteAsync(async transactionCt =>
        {
            var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == id, transactionCt);
            var day = await _db.SubscriptionDays.FirstOrDefaultAsync(x => x.SubscriptionId == id && x.ServiceDate == date, transactionCt);
            if (subscription is null || day is null || subscription.RegionId is null || day.Status is SubscriptionDayStatus.Cancelled or SubscriptionDayStatus.Delivered)
                return new ReassignmentOutcome("reassignment.day_unavailable");

            foreach (var restaurantId in new[] { day.RestaurantId, request.RestaurantId }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().OrderBy(x => x))
                await _capacityLock.AcquireAsync(restaurantId, [date], transactionCt);

            var candidate = (await _eligibility.EvaluateAsync(new RestaurantEligibilityRequest(
                subscription.ProgramId, subscription.BundleId, subscription.SelectedTier, subscription.RegionId.Value,
                date, 1, true, subscription.CustomerUserId, request.RestaurantId), transactionCt)).SingleOrDefault();
            if (candidate is null || !candidate.IsEligible)
                return new ReassignmentOutcome("reassignment.restaurant_not_eligible", candidate?.ReasonCodes);

            var resolvedVersionId = candidate.MenuPlanVersionsByDate.Single(x => x.ServiceDate == date).MenuPlanVersionId;
            day.RestaurantId = request.RestaurantId;
            day.ResolvedMenuPlanVersionId = resolvedVersionId;
            day.Status = SubscriptionDayStatus.ReplacementWindowOpen;
            var hold = await _db.CapacityHolds.FirstOrDefaultAsync(x => x.SubscriptionId == id && x.ServiceDate == date, transactionCt);
            if (hold is not null) { hold.RestaurantId = request.RestaurantId; hold.MenuPlanVersionId = resolvedVersionId; hold.Status = CapacityHoldStatus.Confirmed; }
            var oldSnapshot = await _db.SubscriptionDayMealSnapshots.FirstOrDefaultAsync(x => x.SubscriptionDayId == day.Id, transactionCt);
            if (oldSnapshot is not null) _db.SubscriptionDayMealSnapshots.Remove(oldSnapshot);
            _db.SubscriptionOperations.Add(new SubscriptionOperation { SubscriptionId = id, OperationType = "DAY_REASSIGNED", PreviousStatus = subscription.Status, NewStatus = subscription.Status, Reason = request.Reason, PerformedByUserId = _currentUser.UserId ?? Guid.Empty, PerformedByUserType = _currentUser.UserType ?? UserType.Admin, TimestampUtc = _clock.UtcNow });
            await _db.SaveChangesAsync(transactionCt);
            await _snapshots.CreateMissingAsync(id, transactionCt);
            return new ReassignmentOutcome(null, null, day.Id, request.RestaurantId, resolvedVersionId, day.Status);
        }, cancellationToken);
        return outcome.ErrorCode is null
            ? Ok(new { id = outcome.DayId, restaurantId = outcome.RestaurantId, resolvedMenuPlanVersionId = outcome.MenuPlanVersionId, status = outcome.Status })
            : BadRequest(new { error = outcome.ErrorCode, reasonCodes = outcome.ReasonCodes });
    }
}

public sealed record ReassignRestaurantRequest(Guid RestaurantId, string? Reason = null);
public sealed record ReassignmentOutcome(string? ErrorCode, IReadOnlyList<string>? ReasonCodes = null, Guid? DayId = null, Guid? RestaurantId = null, Guid? MenuPlanVersionId = null, SubscriptionDayStatus? Status = null);
