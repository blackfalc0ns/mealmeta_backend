using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.Freeze;

public sealed record PreparedSubscriptionFreeze(
    IReadOnlyList<SubscriptionDay> SourceDays,
    IReadOnlyList<SubscriptionRestaurantDaySnapshot> RestaurantDays,
    IReadOnlyList<SubscriptionRestaurantDaySnapshot> ReservedDays,
    IReadOnlyDictionary<DateOnly, int> SourceQuantities);

public interface ISubscriptionFreezePreparationService
{
    Task<Result<PreparedSubscriptionFreeze>> PrepareAsync(Subscription subscription, DateOnly start, DateOnly end,
        bool extend, CancellationToken ct = default);
    Task<int> ApplyAsync(Subscription subscription, PreparedSubscriptionFreeze preparation, bool extend, CancellationToken ct = default);
}

/// <summary>Prepares real menus and capacity under the caller's subscription transaction before changing any business state.</summary>
public sealed class SubscriptionFreezePreparationService(
    IAppDbContext db, IDateTimeProvider clock, ISubscriptionScheduleService schedule,
    IOperationalPolicyService policies, IRestaurantEligibilityService eligibility,
    IRestaurantPoolSnapshotService poolSnapshots, ICapacityConcurrencyLock capacityLock,
    IApplicationLockManager locks, IRestaurantCapacityReader capacityReader) : ISubscriptionFreezePreparationService
{
    public async Task<Result<PreparedSubscriptionFreeze>> PrepareAsync(Subscription subscription, DateOnly start,
        DateOnly end, bool extend, CancellationToken ct = default)
    {
        var allDays = await db.SubscriptionDays.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id)
            .OrderBy(x => x.ServiceDate).ToListAsync(ct);
        var source = allDays.Where(x => x.ServiceDate >= start && x.ServiceDate <= end).ToList();
        await OperationalDeadlineRules.RefreshAsync(db, source, policies, allowTrial: false, ct);
        if (source.Any(x => !OperationalDeadlineRules.IsMutable(x) || x.ConfirmedAtUtc != null || x.SelectionDeadlineUtc is not { } deadline || deadline <= clock.UtcNow))
            return Error.Conflict("freeze.locked_days_cannot_be_frozen");
        if (source.Count == 0) return Error.Conflict("freeze.no_service_days");

        var dates = new List<DateOnly>();
        if (extend)
        {
            // Never place compensation inside the existing subscription period.
            var cursor = (allDays[^1].ServiceDate > subscription.EndDate ? allDays[^1].ServiceDate : subscription.EndDate).AddDays(1);
            while (dates.Count < source.Count)
            {
                if (subscription.IncludeFriday || cursor.DayOfWeek != DayOfWeek.Friday) dates.Add(cursor);
                cursor = cursor.AddDays(1);
            }
        }
        var captured = await db.SubscriptionRestaurantSnapshots.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id)
            .OrderByDescending(x => x.IsInitiallySelected).ThenBy(x => x.RestaurantId).ToListAsync(ct);
        if (extend && (captured.Count == 0 || captured.Any(x => x.RegionId == Guid.Empty)))
            return Error.Conflict("freeze.replacement_pool_unavailable");
        var oldHolds = await db.CapacityHolds.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id
            && x.ServiceDate >= start && x.ServiceDate <= end && x.Status != CapacityHoldStatus.Released && x.Status != CapacityHoldStatus.Expired).ToListAsync(ct);
        var keys = captured.SelectMany(x => dates.Select(date => (x.RestaurantId, ServiceDate: date)))
            .Concat(oldHolds.Select(x => (x.RestaurantId, x.ServiceDate)))
            .Concat(source.Where(x => x.RestaurantId != null).Select(x => (x.RestaurantId!.Value, x.ServiceDate)))
            .Distinct().OrderBy(x => x.Item1).ThenBy(x => x.ServiceDate).ToList();
        foreach (var restaurantKeys in keys.GroupBy(x => x.Item1))
            await capacityLock.AcquireAsync(restaurantKeys.Key, restaurantKeys.Select(x => x.ServiceDate).ToList(), ct);
        await locks.LockRestaurantCapacityAsync(keys, ct);
        var capacity = await capacityReader.ReadAsync(keys, ct);
        var prepared = new List<SubscriptionRestaurantDaySnapshot>();
        var reserved = new List<SubscriptionRestaurantDaySnapshot>();
        foreach (var date in dates)
        {
            var dateRows = new List<SubscriptionRestaurantDaySnapshot>();
            var failedReasons = new HashSet<string>();
            foreach (var restaurant in captured)
            {
                var verdicts = await eligibility.EvaluateAsync(new RestaurantEligibilityRequest(subscription.ProgramId,
                    subscription.BundleId, subscription.SelectedTier, source[dates.IndexOf(date)].DeliveryRegionId ?? subscription.RegionId ?? restaurant.RegionId, date, 1,
                    subscription.IncludeFriday, subscription.CustomerUserId, restaurant.RestaurantId, [date]), ct);
                var verdict = verdicts.SingleOrDefault(x => x.RestaurantId == restaurant.RestaurantId);
                if (verdict is null || !verdict.IsEligible)
                {
                    if (verdict is not null) failedReasons.UnionWith(verdict.ReasonCodes);
                    continue;
                }
                if (!capacity.TryGetValue((restaurant.RestaurantId, date), out var units)
                    || units.LimitBoxes is not { } limit || units.TotalCommittedUnits >= limit || limit <= 0)
                { failedReasons.Add(EligibilityReasonCodes.CapacityUnavailable); continue; }
                var menu = verdict.MenuPlanVersionsByDate.SingleOrDefault(x => x.ServiceDate == date);
                if (menu is null) { failedReasons.Add(EligibilityReasonCodes.MenuPlanMissing); continue; }
                var result = await poolSnapshots.PrepareDayAsync(subscription, restaurant, date, menu.MenuPlanVersionId, ct);
                if (result.IsFailure) return result.Error;
                dateRows.Add(result.Value);
            }
            if (dateRows.Count == 0)
                return Error.Conflict(failedReasons.Contains(EligibilityReasonCodes.CapacityUnavailable)
                    ? "freeze.replacement_capacity_unavailable"
                    : failedReasons.Contains(EligibilityReasonCodes.NoSafeMeals) || failedReasons.Contains(EligibilityReasonCodes.MenuSlotIncomplete)
                        ? "freeze.replacement_meals_unavailable"
                        : "freeze.replacement_restaurants_unavailable");
            prepared.AddRange(dateRows);
            reserved.Add(dateRows[0]);
        }
        return new PreparedSubscriptionFreeze(source, prepared, reserved,
            source.ToDictionary(x => x.ServiceDate, x => Math.Max(1, oldHolds.Where(h => h.ServiceDate == x.ServiceDate).Select(h => h.Quantity).DefaultIfEmpty(1).Max())));
    }

    public async Task<int> ApplyAsync(Subscription subscription, PreparedSubscriptionFreeze preparation, bool extend, CancellationToken ct = default)
    {
        var sourceIds = preparation.SourceDays.Select(x => x.Id).ToList();
        var sourceDates = preparation.SourceDays.Select(x => x.ServiceDate).ToList();
        var days = await db.SubscriptionDays.Where(x => sourceIds.Contains(x.Id)).ToListAsync(ct);
        var holds = await db.CapacityHolds.Where(x => x.SubscriptionId == subscription.Id && sourceDates.Contains(x.ServiceDate)
            && x.Status != CapacityHoldStatus.Released && x.Status != CapacityHoldStatus.Expired).ToListAsync(ct);
        foreach (var hold in holds) { hold.Status = CapacityHoldStatus.Released; hold.ReleasedAtUtc = clock.UtcNow; }
        foreach (var day in days)
        {
            day.Status = SubscriptionDayStatus.Frozen; day.RestaurantId = null;
            day.IsAutoSelected = false; day.ModifiedAtUtc = clock.UtcNow;
        }
        db.DailyMealSelections.RemoveRange(await db.DailyMealSelections.IgnoreQueryFilters().Where(x => sourceIds.Contains(x.SubscriptionDayId)).ToListAsync(ct));
        db.DailyRestaurantSelections.RemoveRange(await db.DailyRestaurantSelections.Where(x => sourceIds.Contains(x.SubscriptionDayId)).ToListAsync(ct));
        db.SubscriptionRestaurantDaySnapshots.AddRange(preparation.RestaurantDays);
        foreach (var reserved in preparation.ReservedDays)
        {
            var template = preparation.SourceDays.Single(x => x.Id == sourceIds[preparation.ReservedDays.ToList().IndexOf(reserved)]);
            var day = new SubscriptionDay
            {
                SubscriptionId = subscription.Id, CalendarId = template.CalendarId,
                ServiceDate = reserved.ServiceDate, Status = SubscriptionDayStatus.Open,
                ResolvedMenuPlanVersionId = reserved.MenuPlanVersionId,
                SelectionDeadlineUtc = schedule.GetSelectionDeadlineUtc(reserved.ServiceDate),
                DeliveryAddressId = template.DeliveryAddressId, DeliveryAddressLine = template.DeliveryAddressLine,
                DeliveryRegionId = template.DeliveryRegionId, DeliveryBlockRegionId = template.DeliveryBlockRegionId
            };
            db.SubscriptionDays.Add(day);
            db.SubscriptionDayMealSnapshots.Add(new SubscriptionDayMealSnapshot
            {
                SubscriptionId = subscription.Id, SubscriptionDayId = day.Id, ServiceDate = day.ServiceDate,
                RestaurantId = reserved.RestaurantId, ProgramId = subscription.ProgramId,
                MenuPlanVersionId = reserved.MenuPlanVersionId, RequiredSlotsJson = reserved.SlotRequirementsJson,
                MealsJson = reserved.MealsJson, CapturedAtUtc = clock.UtcNow
            });
            db.CapacityHolds.Add(new CapacityHold
            {
                SubscriptionId = subscription.Id, RestaurantId = reserved.RestaurantId, ServiceDate = reserved.ServiceDate,
                MenuPlanVersionId = reserved.MenuPlanVersionId, Quantity = preparation.SourceQuantities[template.ServiceDate],
                Status = CapacityHoldStatus.Confirmed, ConfirmedAtUtc = clock.UtcNow, ExpiresAtUtc = clock.UtcNow
            });
        }
        if (extend && preparation.ReservedDays.Count > 0) subscription.EndDate = preparation.ReservedDays.Max(x => x.ServiceDate);
        return days.Count;
    }
}
