using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCheckout;

public sealed class AdminCheckoutOperationsService : IAdminCheckoutOperationsService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICapacityConcurrencyLock _capacityLock;
    private readonly ICheckoutTransactionExecutor _transactions;
    private readonly ICapacityHoldExpiryService _expiry;
    private readonly ISubscriptionFulfillmentService _fulfillment;
    private readonly IRestaurantCapacityReader _capacityReader;

    public AdminCheckoutOperationsService(
        IAppDbContext db,
        IDateTimeProvider clock,
        ICapacityConcurrencyLock capacityLock,
        ICheckoutTransactionExecutor transactions,
        ICapacityHoldExpiryService expiry,
        ISubscriptionFulfillmentService fulfillment,
        IRestaurantCapacityReader capacityReader)
    {
        _db = db;
        _clock = clock;
        _capacityLock = capacityLock;
        _transactions = transactions;
        _expiry = expiry;
        _fulfillment = fulfillment;
        _capacityReader = capacityReader;
    }

    public async Task<Result<IReadOnlyList<CapacityHoldItemDto>>> GetCapacityHoldsAsync(CapacityHoldStatus? status, CancellationToken ct = default)
    {
        var query = _db.CapacityHolds.AsNoTracking().AsQueryable();
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);

        var list = await query.OrderBy(x => x.ExpiresAtUtc).Take(500)
            .Select(x => new CapacityHoldItemDto(
                x.Id,
                x.SubscriptionId,
                x.RestaurantId,
                x.ServiceDate,
                x.Quantity,
                x.Status,
                x.ExpiresAtUtc,
                x.ConfirmedAtUtc,
                x.ReleasedAtUtc))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<CapacityHoldItemDto>>(list);
    }

    public async Task<Result<IReadOnlyList<SnapshotMigrationIssueDto>>> GetSnapshotMigrationIssuesAsync(bool unresolvedOnly = true, CancellationToken ct = default)
    {
        var query = _db.SubscriptionSnapshotMigrationIssues.AsNoTracking();
        if (unresolvedOnly) query = query.Where(x => x.ResolvedAtUtc == null);

        var list = await query.OrderByDescending(x => x.RecordedAtUtc).Take(500)
            .Select(x => new SnapshotMigrationIssueDto(
                x.SubscriptionId,
                x.ReasonCode,
                x.DetailsJson,
                x.RecordedAtUtc,
                x.ResolvedAtUtc))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<SnapshotMigrationIssueDto>>(list);
    }

    public async Task<Result<ExpireHoldsOutcome>> ExpireHoldsAsync(CancellationToken ct = default)
    {
        var result = await _expiry.ExpireDueAsync(ct);
        return Result.Success(new ExpireHoldsOutcome(result.HoldsExpired, result.SubscriptionsExpired));
    }

    public async Task<Result<RetryFulfillmentOutcome>> RetryFulfillmentAsync(Guid subscriptionId, CancellationToken ct = default)
    {
        var result = await _fulfillment.TryFulfillAsync(subscriptionId, ct);
        var outcome = new RetryFulfillmentOutcome(
            subscriptionId,
            result.IsSuccess ? "Completed" : "PendingRetry",
            result.IsAlreadyComplete,
            result.ErrorCode);

        return Result.Success(outcome);
    }

    public async Task<Result<ReassignmentOutcome>> ReassignRestaurantAsync(
        Guid subscriptionId,
        DateOnly date,
        ReassignRestaurantRequest request,
        Guid currentUserId,
        UserType currentUserType,
        CancellationToken ct = default)
    {
        var outcome = await _transactions.ExecuteAsync(async transactionCt =>
        {
            await _capacityLock.AcquireSubscriptionAsync(subscriptionId, transactionCt);
            var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == subscriptionId, transactionCt);
            var day = await _db.SubscriptionDays.FirstOrDefaultAsync(x => x.SubscriptionId == subscriptionId && x.ServiceDate == date, transactionCt);
            if (subscription is null || day is null || day.Status is SubscriptionDayStatus.Cancelled or SubscriptionDayStatus.Delivered)
                return new ReassignmentOutcome("reassignment.day_unavailable");

            foreach (var restaurantId in new[] { day.RestaurantId, request.RestaurantId }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().OrderBy(x => x))
                await _capacityLock.AcquireAsync(restaurantId, [date], transactionCt);

            var options = await LoadFrozenRestaurantOptionsInternalAsync(subscriptionId, date, day.RestaurantId, transactionCt);
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
                x => x.SubscriptionId == subscriptionId && x.RestaurantId == request.RestaurantId && x.ServiceDate == date, transactionCt);
            var previousRestaurantId = day.RestaurantId;
            day.RestaurantId = request.RestaurantId;
            day.ResolvedMenuPlanVersionId = source.MenuPlanVersionId;
            day.Status = SubscriptionDayStatus.ReplacementWindowOpen;
            day.LockAtUtc = _clock.UtcNow;
            day.ConfirmedAtUtc = null;

            var hold = await _db.CapacityHolds.FirstOrDefaultAsync(x => x.SubscriptionId == subscriptionId && x.ServiceDate == date, transactionCt);
            if (hold is not null)
            {
                hold.RestaurantId = request.RestaurantId;
                hold.MenuPlanVersionId = source.MenuPlanVersionId;
                hold.Status = CapacityHoldStatus.Confirmed;
            }

            var selections = await _db.DailyMealSelections.Where(x => x.SubscriptionDayId == day.Id).ToListAsync(transactionCt);
            _db.DailyMealSelections.RemoveRange(selections);

            var oldSnapshot = await _db.SubscriptionDayMealSnapshots.FirstOrDefaultAsync(x => x.SubscriptionDayId == day.Id, transactionCt);
            if (oldSnapshot is not null)
                _db.SubscriptionDayMealSnapshots.Remove(oldSnapshot);

            _db.SubscriptionDayMealSnapshots.Add(new SubscriptionDayMealSnapshot
            {
                SubscriptionId = subscriptionId,
                SubscriptionDayId = day.Id,
                ServiceDate = date,
                RestaurantId = request.RestaurantId,
                ProgramId = subscription.ProgramId,
                MenuPlanVersionId = source.MenuPlanVersionId,
                RequiredSlotsJson = source.SlotRequirementsJson,
                MealsJson = source.MealsJson,
                CapturedAtUtc = _clock.UtcNow
            });

            _db.SubscriptionOperations.Add(new SubscriptionOperation
            {
                SubscriptionId = subscriptionId,
                OperationType = "DAY_REASSIGNED",
                PreviousStatus = subscription.Status,
                NewStatus = subscription.Status,
                Reason = $"OldRestaurantId={previousRestaurantId}; NewRestaurantId={request.RestaurantId}; {request.Reason}",
                PerformedByUserId = currentUserId,
                PerformedByUserType = currentUserType,
                TimestampUtc = _clock.UtcNow
            });

            await _db.SaveChangesAsync(transactionCt);
            return new ReassignmentOutcome(null, null, day.Id, request.RestaurantId, source.MenuPlanVersionId, day.Status);
        }, ct);

        return Result.Success(outcome);
    }

    public async Task<Result<IReadOnlyList<FrozenRestaurantOption>>> GetFrozenRestaurantOptionsAsync(Guid subscriptionId, DateOnly date, CancellationToken ct = default)
    {
        var day = await _db.SubscriptionDays.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriptionId == subscriptionId && x.ServiceDate == date, ct);
        if (day is null) return Error.NotFound("subscription_day_not_found");

        var options = await LoadFrozenRestaurantOptionsInternalAsync(subscriptionId, date, day.RestaurantId, ct);
        return Result.Success<IReadOnlyList<FrozenRestaurantOption>>(options);
    }

    private async Task<List<FrozenRestaurantOption>> LoadFrozenRestaurantOptionsInternalAsync(
        Guid subscriptionId,
        DateOnly date,
        Guid? currentRestaurantId,
        CancellationToken ct)
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

            var units = (await _capacityReader.ReadAsync([(item.RestaurantId, date)], ct))[(item.RestaurantId, date)];
            if (units.LimitBoxes is not { } limit || limit <= 0 || units.TotalCommittedUnits >= limit)
                reasons.Add(EligibilityReasonCodes.CapacityUnavailable);
            result.Add(new FrozenRestaurantOption(item.RestaurantId, item.NameAr, item.NameEn, item.MenuPlanVersionId, reasons.Count == 0, reasons));
        }

        return result;
    }
}
