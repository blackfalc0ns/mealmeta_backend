using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCalendar;

public sealed class CustomerCalendarEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly SubscriptionDayStatus[] CountedRestaurantStatuses =
    [
        SubscriptionDayStatus.CustomerSelected,
        SubscriptionDayStatus.AutoSelected,
        SubscriptionDayStatus.Locked72h,
        SubscriptionDayStatus.SentToRestaurant,
        SubscriptionDayStatus.ConfirmedByRestaurant,
        SubscriptionDayStatus.Preparing,
        SubscriptionDayStatus.OutForDelivery,
        SubscriptionDayStatus.Delivered
    ];
    private static readonly SubscriptionDayStatus[] LockedStatuses =
    [
        SubscriptionDayStatus.Locked72h,
        SubscriptionDayStatus.SentToRestaurant,
        SubscriptionDayStatus.ConfirmedByRestaurant,
        SubscriptionDayStatus.Preparing,
        SubscriptionDayStatus.OutForDelivery,
        SubscriptionDayStatus.Delivered,
        SubscriptionDayStatus.Frozen,
        SubscriptionDayStatus.Cancelled,
        SubscriptionDayStatus.Failed
    ];

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IRestaurantDiversificationCalculator _calculator;
    private readonly ISubscriptionDayDispatchService _dispatch;
    private readonly FamilyManager.IFamilyCalendarAccessGate _accessGate;

    public CustomerCalendarEngine(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IRestaurantDiversificationCalculator calculator,
        ISubscriptionDayDispatchService? dispatch = null,
        FamilyManager.IFamilyCalendarAccessGate? accessGate = null)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _calculator = calculator;
        _dispatch = dispatch ?? NullSubscriptionDayDispatchService.Instance;
        _accessGate = accessGate ?? new FamilyManager.FamilyCalendarAccessGate(db, currentUser);
    }

    public Task<Result<CustomerCalendarScreenDto>> GetCalendarAsync(Guid subscriptionId, CancellationToken ct)
        => WithContext(subscriptionId, requireOwner: true, requireWrite: false, ct, async ctx =>
        {
            var days = await MapDaysAsync(ctx, ct);
            return Result.Success(new CustomerCalendarScreenDto(
                ctx.Subscription.Id,
                ctx.Subscription.ProgramId,
                ctx.Subscription.BundleId,
                ctx.RequiredCount,
                ctx.SlotRequirements,
                true,
                days));
        });

    public Task<Result<CustomerDayRestaurantsDto>> GetDayRestaurantsAsync(Guid subscriptionId, DateOnly date, CancellationToken ct)
        => WithContext(subscriptionId, requireOwner: true, requireWrite: false, ct, async ctx =>
        {
            var day = ctx.Day(date);
            if (day is null) return Error.NotFound(MessageCodes.Calendar.DayUnavailable);
            var restaurants = ListRestaurants(ctx, day);
            return Result.Success(new CustomerDayRestaurantsDto(date, ctx.Limit, ctx.AdminCeiling, restaurants));
        });

    public Task<Result<CustomerDayRestaurantMealsDto>> GetRestaurantMealsAsync(
        Guid subscriptionId, DateOnly date, Guid restaurantId, string? slot, CancellationToken ct)
        => WithContext(subscriptionId, requireOwner: true, requireWrite: false, ct, async ctx =>
            MapRestaurantMeals(ctx, date, restaurantId, slot));

    /// <summary>
    /// Compatibility for Flutter clients still calling GET .../days/{date}/meals/available.
    /// Uses the day's selected restaurant, or the first restaurant in the frozen pool.
    /// </summary>
    public Task<Result<CustomerDayRestaurantMealsDto>> GetAvailableMealsAsync(
        Guid subscriptionId, DateOnly date, string? slot, CancellationToken ct)
        => WithContext(subscriptionId, requireOwner: true, requireWrite: false, ct, async ctx =>
        {
            var day = ctx.Day(date);
            if (day is null) return Error.NotFound(MessageCodes.Calendar.DayUnavailable);
            var restaurantId = day.RestaurantId
                ?? ctx.Restaurants.OrderByDescending(x => x.IsInitiallySelected).Select(x => (Guid?)x.RestaurantId).FirstOrDefault();
            if (restaurantId is null)
                return Error.Validation(MessageCodes.Calendar.RestaurantNotInPool);
            return MapRestaurantMeals(ctx, date, restaurantId.Value, slot);
        });

    private static Result<CustomerDayRestaurantMealsDto> MapRestaurantMeals(
        CalendarContext ctx, DateOnly date, Guid restaurantId, string? slot)
    {
        var day = ctx.Day(date);
        if (day is null) return Error.NotFound(MessageCodes.Calendar.DayUnavailable);
        var frozen = ctx.Restaurants.FirstOrDefault(x => x.RestaurantId == restaurantId);
        if (frozen is null) return Error.Validation(MessageCodes.Calendar.RestaurantNotInPool);
        var snapshot = ctx.RestaurantDays.FirstOrDefault(x => x.RestaurantId == restaurantId && x.ServiceDate == date);
        if (snapshot is null) return Error.Conflict(MessageCodes.Calendar.BoxNotReady);
        var selectedIds = day.RestaurantId == restaurantId
            ? ctx.Selections.Where(x => x.SubscriptionDayId == day.Id).Select(x => x.MealId).ToHashSet()
            : [];
        var meals = ParseMeals(snapshot.MealsJson)
            .Where(x => slot is null || x.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase))
            .Select(x => ToMealDto(x, ctx.Preferences, selectedIds.Contains(x.MealId)))
            .OrderBy(x => x.SortOrder)
            .ToList();
        var filled = day.RestaurantId == restaurantId ? CountFilled(ctx, day) : 0;
        return Result.Success(new CustomerDayRestaurantMealsDto(
            restaurantId, frozen.NameAr, frozen.NameEn, date, ctx.RequiredCount, filled, ctx.SlotRequirements, meals,
            selectedIds.ToList()));
    }

    public Task<Result<CustomerCalendarDayDto>> SelectRestaurantAsync(Guid subscriptionId, DateOnly date, Guid restaurantId, CancellationToken ct)
        => WithContext(subscriptionId, requireOwner: true, requireWrite: true, ct, async ctx =>
        {
            var day = ctx.Day(date);
            if (day is null) return Error.NotFound(MessageCodes.Calendar.DayUnavailable);
            var edit = EnsureEditable(ctx, day, allowAfterDeadline: false);
            if (edit.IsFailure) return edit.Error;
            var listed = ListRestaurants(ctx, day);
            var target = listed.FirstOrDefault(x => x.RestaurantId == restaurantId);
            if (target is null) return Error.Validation(MessageCodes.Calendar.RestaurantNotInPool);
            if (!target.IsEnabled)
                return Error.Conflict(target.DisabledReason ?? MessageCodes.Calendar.RestaurantNotInPool);

            if (day.RestaurantId != restaurantId)
            {
                var snapshot = ctx.RestaurantDays.First(x => x.RestaurantId == restaurantId && x.ServiceDate == date);
                ApplyRestaurantSnapshot(ctx, day, snapshot);
                var meals = await _db.DailyMealSelections.Where(x => x.SubscriptionDayId == day.Id).ToListAsync(ct);
                _db.DailyMealSelections.RemoveRange(meals);
                ctx.Selections.RemoveAll(x => x.SubscriptionDayId == day.Id);
                var previous = await _db.DailyRestaurantSelections.Where(x => x.SubscriptionDayId == day.Id).ToListAsync(ct);
                _db.DailyRestaurantSelections.RemoveRange(previous);
                _db.DailyRestaurantSelections.Add(new DailyRestaurantSelection
                {
                    SubscriptionDayId = day.Id,
                    RestaurantId = restaurantId,
                    SelectedByUserId = _currentUser.UserId,
                    SelectedAtUtc = _clock.UtcNow,
                    SelectionSource = "Manual"
                });
                day.Status = SubscriptionDayStatus.Open;
                day.IsAutoSelected = false;
            }

            return Result.Success(await MapDayAsync(ctx, day, ct));
        });

    public Task<Result<CustomerCalendarDayDto>> SelectSlotMealAsync(
        Guid subscriptionId, DateOnly date, Guid mealId, string slot, CancellationToken ct)
        => WithContext(subscriptionId, requireOwner: true, requireWrite: true, ct, async ctx =>
        {
            var day = ctx.Day(date);
            if (day is null) return Error.NotFound(MessageCodes.Calendar.DayUnavailable);
            var edit = EnsureEditable(ctx, day, allowAfterDeadline: false);
            if (edit.IsFailure) return edit.Error;
            if (day.RestaurantId is null) return Error.Validation(MessageCodes.Calendar.DayUnavailable);
            var requirement = ctx.SlotRequirements.FirstOrDefault(x => x.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase));
            if (requirement is null) return Error.Validation(MessageCodes.Calendar.SlotMismatch);
            var meals = CurrentDayMeals(ctx, day);
            var meal = meals.FirstOrDefault(x => x.MealId == mealId);
            if (meal is null) return Error.Validation(MessageCodes.Calendar.InvalidOrRestrictedMeal);
            if (!meal.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase))
                return Error.Validation(MessageCodes.Calendar.SlotMismatch);
            var block = MealPreferenceFilter.BlockReason(meal, ctx.Preferences);
            if (block == MealPreferenceFilter.AllergyConflict)
                return Error.Validation(MessageCodes.Calendar.AllergyConflict);

            var existing = ctx.Selections.Where(x => x.SubscriptionDayId == day.Id).OrderBy(x => x.SortOrder).ToList();
            var slotMeals = existing.Where(x => meals.Any(m => m.MealId == x.MealId && m.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase))).ToList();
            if (existing.Any(x => x.MealId == mealId))
                return Result.Success(await MapDayAsync(ctx, day, ct));
            if (slotMeals.Count >= requirement.Count)
            {
                var replaced = slotMeals[^1];
                _db.DailyMealSelections.Remove(replaced);
                ctx.Selections.Remove(replaced);
                existing.Remove(replaced);
            }

            var selection = new DailyMealSelection
            {
                SubscriptionDayId = day.Id,
                MealId = mealId,
                SortOrder = existing.Count,
                SelectedAtUtc = _clock.UtcNow,
                ResolvedMenuPlanVersionId = day.ResolvedMenuPlanVersionId,
                ResolvedProgramId = ctx.Subscription.ProgramId
            };
            _db.DailyMealSelections.Add(selection);
            ctx.Selections.Add(selection);
            day.Status = SubscriptionDayStatus.Open;
            day.IsAutoSelected = false;
            return Result.Success(await MapDayAsync(ctx, day, ct));
        });

    public Task<Result<CustomerCalendarDayDto>> CompleteBoxAsync(Guid subscriptionId, DateOnly date, string? idempotencyKey, CancellationToken ct)
        => WithContext(subscriptionId, requireOwner: true, requireWrite: true, ct, async ctx =>
        {
            var day = ctx.Day(date);
            if (day is null) return Error.NotFound(MessageCodes.Calendar.DayUnavailable);
            if (day.Status == SubscriptionDayStatus.CustomerSelected && IsBoxComplete(ctx, day))
                return Result.Success(await MapDayAsync(ctx, day, ct));
            var edit = EnsureEditable(ctx, day, allowAfterDeadline: false);
            if (edit.IsFailure) return edit.Error;
            if (!IsBoxComplete(ctx, day))
                return Error.Validation(MessageCodes.Calendar.BoxIncomplete);
            day.Status = SubscriptionDayStatus.CustomerSelected;
            day.IsAutoSelected = false;
            return Result.Success(await MapDayAsync(ctx, day, ct));
        });

    public Task<Result<CustomerCalendarDayDto>> AutoSelectFullBoxAsync(
        Guid subscriptionId, DateOnly date, bool lockAfter, bool requireOwner, CancellationToken ct)
        => WithContext(subscriptionId, requireOwner, requireWrite: true, ct, async ctx =>
        {
            var day = ctx.Day(date);
            if (day is null) return Error.NotFound(MessageCodes.Calendar.DayUnavailable);
            if (day.Status is SubscriptionDayStatus.Cancelled or SubscriptionDayStatus.Frozen)
                return Error.Conflict(MessageCodes.Calendar.DayUnavailable);

            var pastDeadline = day.SelectionDeadlineUtc is { } deadline && _clock.UtcNow >= deadline;
            if (!lockAfter)
            {
                var edit = EnsureEditable(ctx, day, allowAfterDeadline: false);
                if (edit.IsFailure) return edit.Error;
            }
            else if (LockedStatuses.Contains(day.Status) && day.Status != SubscriptionDayStatus.Locked72h)
                return Error.Conflict(MessageCodes.Calendar.DayUnavailable);

            if (IsBoxComplete(ctx, day) && (day.Status == SubscriptionDayStatus.CustomerSelected || day.Status == SubscriptionDayStatus.AutoSelected || lockAfter))
            {
                if (lockAfter)
                    await _dispatch.LockAndSendToRestaurantAsync(day, ctx.Subscription.Id, ct);
                return Result.Success(await MapDayAsync(ctx, day, ct));
            }

            var picked = TryPickFullBox(ctx, day);
            if (picked is null)
            {
                day.Status = SubscriptionDayStatus.ReplacementRequired;
                day.LockAtUtc = null;
                return Error.Conflict(MessageCodes.Calendar.NoSafeBox);
            }

            ApplyRestaurantSnapshot(ctx, day, picked.Snapshot);
            var old = await _db.DailyMealSelections.Where(x => x.SubscriptionDayId == day.Id).ToListAsync(ct);
            _db.DailyMealSelections.RemoveRange(old);
            ctx.Selections.RemoveAll(x => x.SubscriptionDayId == day.Id);
            var index = 0;
            foreach (var meal in picked.Meals)
            {
                var selection = new DailyMealSelection
                {
                    SubscriptionDayId = day.Id,
                    MealId = meal.MealId,
                    SortOrder = index++,
                    SelectedAtUtc = _clock.UtcNow,
                    ResolvedMenuPlanVersionId = day.ResolvedMenuPlanVersionId,
                    ResolvedProgramId = ctx.Subscription.ProgramId
                };
                _db.DailyMealSelections.Add(selection);
                ctx.Selections.Add(selection);
            }

            day.IsAutoSelected = true;
            if (lockAfter || pastDeadline)
                await _dispatch.LockAndSendToRestaurantAsync(day, ctx.Subscription.Id, ct);
            else
                day.Status = SubscriptionDayStatus.AutoSelected;
            return Result.Success(await MapDayAsync(ctx, day, ct));
        });

    private async Task<Result<T>> WithContext<T>(
        Guid subscriptionId,
        bool requireOwner,
        bool requireWrite,
        CancellationToken ct,
        Func<CalendarContext, Task<Result<T>>> action)
    {
        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == subscriptionId, ct);
        if (subscription is null) return Error.NotFound(MessageCodes.Common.NotFound);

        if (requireOwner)
        {
            var auth = await _accessGate.AuthorizeAsync(subscription, requireWrite, ct);
            if (auth.IsFailure) return auth.Error;
        }

        if (subscription.FulfillmentCompletedAtUtc is null)
            return Error.Conflict(MessageCodes.Calendar.FulfillmentNotReady);

        var days = await _db.SubscriptionDays.Where(x => x.SubscriptionId == subscriptionId).OrderBy(x => x.ServiceDate).ToListAsync(ct);
        var dayIds = days.Select(x => x.Id).ToList();
        var selections = await _db.DailyMealSelections.Where(x => dayIds.Contains(x.SubscriptionDayId)).ToListAsync(ct);
        var daySnapshots = await _db.SubscriptionDayMealSnapshots.Where(x => x.SubscriptionId == subscriptionId).ToListAsync(ct);
        var restaurants = await _db.SubscriptionRestaurantSnapshots.AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId).ToListAsync(ct);
        var restaurantDays = await _db.SubscriptionRestaurantDaySnapshots.AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId).ToListAsync(ct);
        var busy = await _db.BusyStatuses.AsNoTracking()
            .Where(x => restaurants.Select(r => r.RestaurantId).Contains(x.RestaurantId) && x.IsBusy)
            .ToListAsync(ct);
        var preferences = await MealPreferenceFilter.LoadAsync(_db, subscription.CustomerUserId, ct);
        var adminCeiling = await ReadAdminCeilingAsync(ct);
        var limit = _calculator.CalculateLimit(subscription.Id, subscription.DurationDays, Math.Max(1, restaurants.Count), adminCeiling);
        var slotRequirements = SlotRequirementJson.Parse(
            restaurantDays.FirstOrDefault()?.SlotRequirementsJson
            ?? daySnapshots.FirstOrDefault()?.RequiredSlotsJson);
        if (slotRequirements.Count == 0)
        {
            var bundle = await _db.Bundles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == subscription.BundleId, ct);
            if (bundle is not null)
            {
                slotRequirements = new[]
                {
                    new SlotRequirementDto("breakfast", bundle.BreakfastCount),
                    new SlotRequirementDto("lunch", bundle.LunchCount),
                    new SlotRequirementDto("dinner", bundle.DinnerCount),
                    new SlotRequirementDto("snack", bundle.SnackCount)
                }.Where(x => x.Count > 0).ToList();
            }
        }

        var ctx = new CalendarContext(subscription, days, selections, daySnapshots, restaurants, restaurantDays, busy, preferences, limit.LimitValue, adminCeiling, slotRequirements);
        return await action(ctx);
    }

    private async Task<int> ReadAdminCeilingAsync(CancellationToken ct)
    {
        var row = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == CustomerCalendarSettings.RepetitionCeilingKey && x.IsActive, ct);
        if (row is null) return CustomerCalendarSettings.DefaultAdminCeiling;
        var raw = row.ValueJson.Trim().Trim('"');
        return int.TryParse(raw, out var value) && value > 0
            ? Math.Max(CustomerCalendarSettings.HardMinLimit, value)
            : CustomerCalendarSettings.DefaultAdminCeiling;
    }

    private Result EnsureEditable(CalendarContext ctx, SubscriptionDay day, bool allowAfterDeadline)
    {
        if (day.Status == SubscriptionDayStatus.Cancelled) return Error.Conflict(MessageCodes.Calendar.DayUnavailable);
        if (LockedStatuses.Contains(day.Status)) return Error.Conflict(MessageCodes.Calendar.SelectionLocked);
        if (!allowAfterDeadline && day.SelectionDeadlineUtc is { } deadline && _clock.UtcNow >= deadline)
            return Error.Conflict(MessageCodes.Calendar.SelectionLocked);
        return Result.Success();
    }

    private static IReadOnlyList<CustomerDayRestaurantDto> ListRestaurants(CalendarContext ctx, SubscriptionDay day)
    {
        var usage = ctx.Days
            .Where(x => x.Id != day.Id && x.RestaurantId is not null && CountedRestaurantStatuses.Contains(x.Status))
            .GroupBy(x => x.RestaurantId!.Value)
            .ToDictionary(x => x.Key, x => x.Count());

        var rows = new List<CustomerDayRestaurantDto>(ctx.Restaurants.Count);
        foreach (var restaurant in ctx.Restaurants.OrderByDescending(x => x.IsInitiallySelected).ThenBy(x => x.NameEn))
        {
            var snapshot = ctx.RestaurantDays.FirstOrDefault(x => x.RestaurantId == restaurant.RestaurantId && x.ServiceDate == day.ServiceDate);
            var meals = snapshot is null ? [] : ParseMeals(snapshot.MealsJson);
            var safeMeals = meals.Where(x => MealPreferenceFilter.BlockReason(x, ctx.Preferences) != MealPreferenceFilter.AllergyConflict).ToList();
            var used = usage.GetValueOrDefault(restaurant.RestaurantId);
            var busy = IsBusy(ctx, restaurant.RestaurantId, day.ServiceDate);
            var allergyCovered = snapshot is not null && CanCoverSlots(safeMeals, ctx.SlotRequirements, allergyOnly: true, ctx.Preferences);
            string? disabled = null;
            if (snapshot is null) disabled = MessageCodes.Calendar.BoxNotReady;
            else if (!allergyCovered) disabled = MessageCodes.Calendar.AllergyUncovered;
            rows.Add(new CustomerDayRestaurantDto(
                restaurant.RestaurantId, restaurant.NameAr, restaurant.NameEn, restaurant.Tier,
                used, ctx.Limit, Math.Max(0, ctx.Limit - used), busy, used >= ctx.Limit, false, disabled, safeMeals.Count));
        }

        var alternativesUnderCap = rows
            .Where(x => x.DisabledReason is null && !x.IsBusy && !x.IsQuotaExhausted)
            .Select(x => x.RestaurantId)
            .ToHashSet();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.DisabledReason is not null)
            {
                rows[i] = row with { IsEnabled = false };
                continue;
            }

            string? disabled = null;
            if (row.IsQuotaExhausted && alternativesUnderCap.Any(id => id != row.RestaurantId))
                disabled = MessageCodes.Calendar.QuotaExhausted;
            else if (row.IsBusy && alternativesUnderCap.Any(id => id != row.RestaurantId))
                disabled = MessageCodes.Calendar.RestaurantBusy;

            rows[i] = row with { IsEnabled = disabled is null, DisabledReason = disabled };
        }

        return rows;
    }

    private PickedBox? TryPickFullBox(CalendarContext ctx, SubscriptionDay day)
    {
        var listed = ListRestaurants(ctx, day);
        var ordered = listed
            .Where(x => x.DisabledReason is null || x.DisabledReason == MessageCodes.Calendar.RestaurantBusy || x.DisabledReason == MessageCodes.Calendar.QuotaExhausted)
            .OrderBy(x => day.RestaurantId == x.RestaurantId ? 0 : 1)
            .ThenBy(x => x.IsBusy)
            .ThenBy(x => x.IsQuotaExhausted)
            .ThenByDescending(x => DietaryScore(ctx, day, x.RestaurantId))
            .ThenByDescending(x => DislikeFreeScore(ctx, day, x.RestaurantId))
            .ToList();

        // Prefer completing the current restaurant when it can still yield a full safe box and is under cap.
        if (day.RestaurantId is { } currentId)
        {
            var current = ordered.FirstOrDefault(x => x.RestaurantId == currentId && !x.IsQuotaExhausted && x.DisabledReason is null);
            if (current is not null)
            {
                var currentBox = BuildFullBox(ctx, day, currentId, keepExisting: true);
                if (currentBox is not null) return currentBox;
            }
        }

        foreach (var restaurant in ordered)
        {
            if (restaurant.DisabledReason == MessageCodes.Calendar.AllergyUncovered || restaurant.DisabledReason == MessageCodes.Calendar.BoxNotReady)
                continue;
            if (restaurant.IsQuotaExhausted && ordered.Any(x => !x.IsQuotaExhausted && x.DisabledReason is null && !x.IsBusy))
                continue;
            var box = BuildFullBox(ctx, day, restaurant.RestaurantId, keepExisting: restaurant.RestaurantId == day.RestaurantId);
            if (box is not null) return box;
        }

        return null;
    }

    private PickedBox? BuildFullBox(CalendarContext ctx, SubscriptionDay day, Guid restaurantId, bool keepExisting)
    {
        var snapshot = ctx.RestaurantDays.FirstOrDefault(x => x.RestaurantId == restaurantId && x.ServiceDate == day.ServiceDate);
        if (snapshot is null) return null;
        var meals = ParseMeals(snapshot.MealsJson);
        var picked = new List<MealSnapshotItem>();
        var existingIds = keepExisting
            ? ctx.Selections.Where(x => x.SubscriptionDayId == day.Id).Select(x => x.MealId).ToHashSet()
            : [];
        foreach (var requirement in ctx.SlotRequirements)
        {
            var kept = meals
                .Where(x => existingIds.Contains(x.MealId)
                    && x.Slot.Equals(requirement.Slot, StringComparison.OrdinalIgnoreCase)
                    && MealPreferenceFilter.BlockReason(x, ctx.Preferences) != MealPreferenceFilter.AllergyConflict)
                .Take(requirement.Count)
                .ToList();
            picked.AddRange(kept);
            var missing = requirement.Count - kept.Count;
            if (missing <= 0) continue;
            var choices = meals
                .Where(x => x.Slot.Equals(requirement.Slot, StringComparison.OrdinalIgnoreCase))
                .Where(x => MealPreferenceFilter.BlockReason(x, ctx.Preferences) != MealPreferenceFilter.AllergyConflict)
                .Where(x => picked.All(p => p.MealId != x.MealId))
                .OrderBy(x => MealPreferenceFilter.BlockReason(x, ctx.Preferences) == MealPreferenceFilter.DietaryPreferenceConflict ? 1 : 0)
                .ThenBy(x => MealPreferenceFilter.BlockReason(x, ctx.Preferences) == MealPreferenceFilter.IngredientPreferenceConflict ? 1 : 0)
                .ThenBy(x => x.SortOrder)
                .Take(missing)
                .ToList();
            if (choices.Count < missing) return null;
            picked.AddRange(choices);
        }

        return new PickedBox(snapshot, picked);
    }

    private int DietaryScore(CalendarContext ctx, SubscriptionDay day, Guid restaurantId)
    {
        var snapshot = ctx.RestaurantDays.FirstOrDefault(x => x.RestaurantId == restaurantId && x.ServiceDate == day.ServiceDate);
        if (snapshot is null) return 0;
        return ParseMeals(snapshot.MealsJson).Count(x => MealPreferenceFilter.BlockReason(x, ctx.Preferences) is null);
    }

    private int DislikeFreeScore(CalendarContext ctx, SubscriptionDay day, Guid restaurantId)
        => DietaryScore(ctx, day, restaurantId);

    private void ApplyRestaurantSnapshot(CalendarContext ctx, SubscriptionDay day, SubscriptionRestaurantDaySnapshot snapshot)
    {
        day.RestaurantId = snapshot.RestaurantId;
        day.ResolvedMenuPlanVersionId = snapshot.MenuPlanVersionId;
        var existing = ctx.DaySnapshots.FirstOrDefault(x => x.SubscriptionDayId == day.Id);
        if (existing is null)
        {
            existing = new SubscriptionDayMealSnapshot
            {
                SubscriptionId = ctx.Subscription.Id,
                SubscriptionDayId = day.Id,
                ServiceDate = day.ServiceDate,
                RestaurantId = snapshot.RestaurantId,
                ProgramId = ctx.Subscription.ProgramId,
                MenuPlanVersionId = snapshot.MenuPlanVersionId,
                RequiredSlotsJson = snapshot.SlotRequirementsJson,
                MealsJson = snapshot.MealsJson,
                CapturedAtUtc = _clock.UtcNow
            };
            _db.SubscriptionDayMealSnapshots.Add(existing);
            ctx.DaySnapshots.Add(existing);
        }
        else
        {
            existing.RestaurantId = snapshot.RestaurantId;
            existing.MenuPlanVersionId = snapshot.MenuPlanVersionId;
            existing.RequiredSlotsJson = snapshot.SlotRequirementsJson;
            existing.MealsJson = snapshot.MealsJson;
            existing.CapturedAtUtc = _clock.UtcNow;
        }
    }

    private async Task<IReadOnlyList<CustomerCalendarDayDto>> MapDaysAsync(CalendarContext ctx, CancellationToken ct)
    {
        var result = new List<CustomerCalendarDayDto>(ctx.Days.Count);
        foreach (var day in ctx.Days)
            result.Add(await MapDayAsync(ctx, day, ct));
        return result;
    }

    private Task<CustomerCalendarDayDto> MapDayAsync(CalendarContext ctx, SubscriptionDay day, CancellationToken ct)
    {
        var frozen = day.RestaurantId is { } restaurantId
            ? ctx.Restaurants.FirstOrDefault(x => x.RestaurantId == restaurantId)
            : null;
        var meals = CurrentDayMeals(ctx, day);
        var selected = ctx.Selections.Where(x => x.SubscriptionDayId == day.Id).OrderBy(x => x.SortOrder)
            .Select(selection =>
            {
                var meal = meals.FirstOrDefault(x => x.MealId == selection.MealId);
                return meal is null
                    ? new CustomerCalendarMealDto(selection.MealId, "", "", "", selection.SortOrder, null, null, null, null, null, null, null, [], [], [], [], null, IsSelected: true)
                    : ToMealDto(meal, ctx.Preferences, isSelected: true);
            })
            .ToList();
        var filled = selected.Count;
        var remaining = day.SelectionDeadlineUtc is { } deadline
            ? (int)Math.Max(0, Math.Ceiling((deadline - _clock.UtcNow).TotalSeconds))
            : (int?)null;
        var canEdit = !LockedStatuses.Contains(day.Status)
            && (day.SelectionDeadlineUtc is null || _clock.UtcNow < day.SelectionDeadlineUtc);
        return Task.FromResult(new CustomerCalendarDayDto(
            day.Id, day.ServiceDate, day.RestaurantId, frozen?.NameAr, frozen?.NameEn,
            day.Status, MapUiStatus(day, filled), day.IsAutoSelected, canEdit,
            day.SelectionDeadlineUtc, day.LockAtUtc, remaining, filled, ctx.RequiredCount,
            ctx.SlotRequirements,
            day.Status == SubscriptionDayStatus.ReplacementRequired ? MessageCodes.Calendar.NoSafeBox : null,
            selected));
    }

    private static string MapUiStatus(SubscriptionDay day, int filled)
    {
        if (day.Status == SubscriptionDayStatus.Cancelled) return CustomerCalendarUiStatus.Cancelled;
        if (day.Status == SubscriptionDayStatus.Frozen) return CustomerCalendarUiStatus.Frozen;
        if (day.Status is SubscriptionDayStatus.ReplacementRequired or SubscriptionDayStatus.ReplacementWindowOpen)
            return CustomerCalendarUiStatus.ReplacementRequired;
        if (day.Status is SubscriptionDayStatus.Locked72h or SubscriptionDayStatus.SentToRestaurant
            or SubscriptionDayStatus.ConfirmedByRestaurant or SubscriptionDayStatus.Preparing
            or SubscriptionDayStatus.OutForDelivery or SubscriptionDayStatus.Delivered)
            return CustomerCalendarUiStatus.Locked;
        if (day.Status == SubscriptionDayStatus.AutoSelected || (day.IsAutoSelected && day.Status != SubscriptionDayStatus.CustomerSelected))
            return CustomerCalendarUiStatus.AutoSelected;
        if (day.Status == SubscriptionDayStatus.CustomerSelected) return CustomerCalendarUiStatus.AwaitingRestaurant;
        if (filled == 0 && day.RestaurantId is null) return CustomerCalendarUiStatus.Empty;
        return CustomerCalendarUiStatus.Drafting;
    }

    private static List<MealSnapshotItem> CurrentDayMeals(CalendarContext ctx, SubscriptionDay day)
    {
        if (day.RestaurantId is { } restaurantId)
        {
            var restaurantDay = ctx.RestaurantDays.FirstOrDefault(x => x.RestaurantId == restaurantId && x.ServiceDate == day.ServiceDate);
            if (restaurantDay is not null) return ParseMeals(restaurantDay.MealsJson);
        }

        var snapshot = ctx.DaySnapshots.FirstOrDefault(x => x.SubscriptionDayId == day.Id);
        return snapshot is null ? [] : ParseMeals(snapshot.MealsJson);
    }

    private static int CountFilled(CalendarContext ctx, SubscriptionDay day)
        => ctx.Selections.Count(x => x.SubscriptionDayId == day.Id);

    private static bool IsBoxComplete(CalendarContext ctx, SubscriptionDay day)
    {
        if (day.RestaurantId is null) return false;
        var meals = CurrentDayMeals(ctx, day);
        var selected = ctx.Selections.Where(x => x.SubscriptionDayId == day.Id).ToList();
        if (selected.Count != ctx.RequiredCount) return false;
        foreach (var requirement in ctx.SlotRequirements)
        {
            var count = selected.Count(selection => meals.Any(meal =>
                meal.MealId == selection.MealId && meal.Slot.Equals(requirement.Slot, StringComparison.OrdinalIgnoreCase)));
            if (count != requirement.Count) return false;
        }

        return true;
    }

    private static bool CanCoverSlots(
        IReadOnlyList<MealSnapshotItem> meals,
        IReadOnlyList<SlotRequirementDto> requirements,
        bool allergyOnly,
        MealPreferenceFilter.CustomerPreferenceState preferences)
    {
        foreach (var requirement in requirements)
        {
            var count = meals.Count(x =>
                x.Slot.Equals(requirement.Slot, StringComparison.OrdinalIgnoreCase)
                && (!allergyOnly || MealPreferenceFilter.BlockReason(x, preferences) != MealPreferenceFilter.AllergyConflict));
            if (count < requirement.Count) return false;
        }

        return requirements.Count > 0;
    }

    private static bool IsBusy(CalendarContext ctx, Guid restaurantId, DateOnly date)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return ctx.Busy.Any(x => x.RestaurantId == restaurantId && x.IsBusy && x.StartsAtUtc < end && (x.EndsAtUtc is null || x.EndsAtUtc > start));
    }

    private static List<MealSnapshotItem> ParseMeals(string json)
        => JsonSerializer.Deserialize<List<MealSnapshotItem>>(json, JsonOptions) ?? [];

    private static CustomerCalendarMealDto ToMealDto(
        MealSnapshotItem meal,
        MealPreferenceFilter.CustomerPreferenceState preferences,
        bool isSelected = false)
        => new(
            meal.MealId, meal.NameAr, meal.NameEn, meal.Slot, meal.SortOrder, meal.ImageUrl,
            meal.Calories, meal.ProteinG, meal.CarbsG, meal.FatG, meal.DescriptionAr, meal.DescriptionEn,
            meal.PresentAllergenCodes, meal.MayContainAllergenCodes, meal.DietaryPreferenceCodes,
            meal.Ingredients.Select(i => new CustomerMealIngredientDto(
                i.Id, i.NameAr, i.NameEn,
                i.IsOriginVisibleToCustomers ? i.OriginCountryCode : null,
                i.IsOriginVisibleToCustomers ? i.SupplierName : null,
                i.IsLocal, i.IsHalalCertified, i.AllergenCodes)).ToList(),
            MealPreferenceFilter.BlockReason(meal, preferences),
            isSelected);

    private sealed record PickedBox(SubscriptionRestaurantDaySnapshot Snapshot, IReadOnlyList<MealSnapshotItem> Meals);

    private sealed class CalendarContext
    {
        public CalendarContext(
            Subscription subscription,
            List<SubscriptionDay> days,
            List<DailyMealSelection> selections,
            List<SubscriptionDayMealSnapshot> daySnapshots,
            List<SubscriptionRestaurantSnapshot> restaurants,
            List<SubscriptionRestaurantDaySnapshot> restaurantDays,
            List<MealMate.Domain.RestaurantOperations.BusyStatus> busy,
            MealPreferenceFilter.CustomerPreferenceState preferences,
            int limit,
            int adminCeiling,
            IReadOnlyList<SlotRequirementDto> slotRequirements)
        {
            Subscription = subscription;
            Days = days;
            Selections = selections;
            DaySnapshots = daySnapshots;
            Restaurants = restaurants;
            RestaurantDays = restaurantDays;
            Busy = busy;
            Preferences = preferences;
            Limit = limit;
            AdminCeiling = adminCeiling;
            SlotRequirements = slotRequirements;
        }

        public Subscription Subscription { get; }
        public List<SubscriptionDay> Days { get; }
        public List<DailyMealSelection> Selections { get; }
        public List<SubscriptionDayMealSnapshot> DaySnapshots { get; }
        public List<SubscriptionRestaurantSnapshot> Restaurants { get; }
        public List<SubscriptionRestaurantDaySnapshot> RestaurantDays { get; }
        public List<MealMate.Domain.RestaurantOperations.BusyStatus> Busy { get; }
        public MealPreferenceFilter.CustomerPreferenceState Preferences { get; }
        public int Limit { get; }
        public int AdminCeiling { get; }
        public IReadOnlyList<SlotRequirementDto> SlotRequirements { get; }
        public int RequiredCount => SlotRequirements.Sum(x => x.Count);
        public SubscriptionDay? Day(DateOnly date) => Days.FirstOrDefault(x => x.ServiceDate == date);
    }
}
