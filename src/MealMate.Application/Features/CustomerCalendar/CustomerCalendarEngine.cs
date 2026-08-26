using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
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
            var days = await MapDaySummariesAsync(ctx, ct);
            return Result.Success(new CustomerCalendarScreenDto(
                ctx.Subscription.Id,
                ctx.Subscription.ProgramId,
                ctx.Subscription.BundleId,
                ctx.RequiredCount,
                CustomerCalendarSlotLabels.ToDtos(ctx.SlotRequirements),
                true,
                days));
        });

    public Task<Result<CustomerCalendarDayDto>> GetDayAsync(Guid subscriptionId, DateOnly date, CancellationToken ct)
        => WithContext(subscriptionId, requireOwner: true, requireWrite: false, ct, async ctx =>
        {
            var day = ctx.Day(date);
            if (day is null) return Error.NotFound(MessageCodes.Calendar.DayUnavailable);
            return Result.Success(await MapDayAsync(ctx, day, ct));
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
            await MapRestaurantMealsAsync(ctx, date, restaurantId, slot, ct));

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
            return await MapRestaurantMealsAsync(ctx, date, restaurantId.Value, slot, ct);
        });

    private async Task<Result<CustomerDayRestaurantMealsDto>> MapRestaurantMealsAsync(
        CalendarContext ctx, DateOnly date, Guid restaurantId, string? slot, CancellationToken ct)
    {
        var day = ctx.Day(date);
        if (day is null) return Error.NotFound(MessageCodes.Calendar.DayUnavailable);
        var frozen = ctx.Restaurants.FirstOrDefault(x => x.RestaurantId == restaurantId);
        if (frozen is null) return Error.Validation(MessageCodes.Calendar.RestaurantNotInPool);
        var snapshot = ctx.RestaurantDays.FirstOrDefault(x => x.RestaurantId == restaurantId && x.ServiceDate == date);
        if (snapshot is null) return Error.Conflict(MessageCodes.Calendar.BoxNotReady);
        var allowedSlots = ctx.SlotRequirements
            .Select(x => x.Slot)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedIds = (EffectiveCustomerRestaurantId(ctx, day) ?? day.RestaurantId) == restaurantId
            ? ctx.Selections.Where(x => x.SubscriptionDayId == day.Id).Select(x => x.MealId).ToHashSet()
            : [];
        var parsed = ParseMeals(snapshot.MealsJson)
            .Where(x => allowedSlots.Contains(x.Slot))
            .Where(x => slot is null || x.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var media = await LoadIngredientMediaAsync(parsed.SelectMany(x => x.Ingredients.Select(i => i.Id)), ct);
        var meals = parsed
            .Select(x => ToMealDto(x, ctx.Preferences, selectedIds.Contains(x.MealId), media))
            .OrderBy(x => x.SortOrder)
            .ToList();
        var coreIngredients = await LoadRestaurantCoreIngredientsAsync(restaurantId, ct);
        var filled = (EffectiveCustomerRestaurantId(ctx, day) ?? day.RestaurantId) == restaurantId
            ? CountFilled(ctx, day)
            : 0;
        return Result.Success(new CustomerDayRestaurantMealsDto(
            RestaurantId: restaurantId,
            NameAr: frozen.NameAr,
            NameEn: frozen.NameEn,
            LogoUrl: ctx.LogoUrl(restaurantId),
            ServiceDate: date,
            RequiredCount: ctx.RequiredCount,
            FilledCount: filled,
            SlotRequirements: CustomerCalendarSlotLabels.ToDtos(ctx.SlotRequirements),
            CoreIngredients: coreIngredients,
            Meals: meals,
            SelectedMealIds: selectedIds.ToList()));
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

            // Re-opening a day must not wipe meals when the customer already confirmed this restaurant.
            var alreadyPicked = day.RestaurantId == restaurantId
                && ctx.RestaurantPicks.Any(x => x.SubscriptionDayId == day.Id && x.RestaurantId == restaurantId);
            if (alreadyPicked)
                return Result.Success(await MapDayAsync(ctx, day, ct));

            if (day.RestaurantId != restaurantId)
            {
                var snapshot = ctx.RestaurantDays.First(x => x.RestaurantId == restaurantId && x.ServiceDate == date);
                ApplyRestaurantSnapshot(ctx, day, snapshot);
                var meals = await _db.DailyMealSelections.IgnoreQueryFilters()
                    .Where(x => x.SubscriptionDayId == day.Id)
                    .ToListAsync(ct);
                await HardDeleteMealSelectionsAsync(meals, ct);
                ctx.Selections.RemoveAll(x => x.SubscriptionDayId == day.Id);
                day.Status = SubscriptionDayStatus.Open;
                day.IsAutoSelected = false;
            }

            var previous = await _db.DailyRestaurantSelections.Where(x => x.SubscriptionDayId == day.Id).ToListAsync(ct);
            _db.DailyRestaurantSelections.RemoveRange(previous);
            var restaurantPick = new DailyRestaurantSelection
            {
                SubscriptionDayId = day.Id,
                RestaurantId = restaurantId,
                SelectedByUserId = _currentUser.UserId,
                SelectedAtUtc = _clock.UtcNow,
                SelectionSource = "Manual"
            };
            _db.DailyRestaurantSelections.Add(restaurantPick);
            ctx.RestaurantPicks.RemoveAll(x => x.SubscriptionDayId == day.Id);
            ctx.RestaurantPicks.Add(restaurantPick);
            if (day.RestaurantId is null)
            {
                var snapshot = ctx.RestaurantDays.First(x => x.RestaurantId == restaurantId && x.ServiceDate == date);
                ApplyRestaurantSnapshot(ctx, day, snapshot);
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
            if (EffectiveCustomerRestaurantId(ctx, day) is null)
                return Error.Validation(MessageCodes.Calendar.DayUnavailable);
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
                await HardDeleteMealSelectionsAsync([replaced], ct);
                ctx.Selections.Remove(replaced);
                existing.Remove(replaced);
            }

            // Soft-deleted rows still occupy the unique (day, meal) index; revive instead of insert.
            var tombstone = await _db.DailyMealSelections.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.SubscriptionDayId == day.Id && x.MealId == mealId, ct);
            if (tombstone is not null)
            {
                tombstone.IsDeleted = false;
                tombstone.DeletedAtUtc = null;
                tombstone.DeletedByUserId = null;
                tombstone.SortOrder = existing.Count;
                tombstone.SelectedAtUtc = _clock.UtcNow;
                tombstone.ResolvedMenuPlanVersionId = day.ResolvedMenuPlanVersionId;
                tombstone.ResolvedProgramId = ctx.Subscription.ProgramId;
                if (!ctx.Selections.Contains(tombstone))
                    ctx.Selections.Add(tombstone);
            }
            else
            {
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
            }

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
            var old = await _db.DailyMealSelections.IgnoreQueryFilters()
                .Where(x => x.SubscriptionDayId == day.Id)
                .ToListAsync(ct);
            await HardDeleteMealSelectionsAsync(old, ct);
            ctx.Selections.RemoveAll(x => x.SubscriptionDayId == day.Id);
            var previousPicks = await _db.DailyRestaurantSelections.Where(x => x.SubscriptionDayId == day.Id).ToListAsync(ct);
            _db.DailyRestaurantSelections.RemoveRange(previousPicks);
            var autoPick = new DailyRestaurantSelection
            {
                SubscriptionDayId = day.Id,
                RestaurantId = picked.Snapshot.RestaurantId,
                SelectedByUserId = _currentUser.UserId,
                SelectedAtUtc = _clock.UtcNow,
                SelectionSource = "Auto"
            };
            _db.DailyRestaurantSelections.Add(autoPick);
            ctx.RestaurantPicks.RemoveAll(x => x.SubscriptionDayId == day.Id);
            ctx.RestaurantPicks.Add(autoPick);
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

    private async Task HardDeleteMealSelectionsAsync(
        IReadOnlyCollection<DailyMealSelection> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0) return;

        var ids = rows.Select(x => x.Id).Distinct().ToList();
        try
        {
            await _db.DailyMealSelections.IgnoreQueryFilters()
                .Where(x => ids.Contains(x.Id))
                .ExecuteDeleteAsync(ct);

            if (_db is DbContext ef)
            {
                foreach (var row in rows)
                {
                    var entry = ef.Entry(row);
                    if (entry.State != EntityState.Detached)
                        entry.State = EntityState.Detached;
                }
            }
        }
        catch (InvalidOperationException)
        {
            // InMemory provider: soft-delete fallback is acceptable for tests.
            _db.DailyMealSelections.RemoveRange(rows);
        }
    }

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
        var restaurantPicks = await _db.DailyRestaurantSelections.Where(x => dayIds.Contains(x.SubscriptionDayId)).ToListAsync(ct);
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

        var logos = await LoadRestaurantLogoUrlsAsync(restaurants.Select(x => x.RestaurantId).ToList(), ct);
        var ctx = new CalendarContext(
            subscription, days, selections, restaurantPicks, daySnapshots, restaurants, restaurantDays, busy, preferences,
            limit.LimitValue, adminCeiling, slotRequirements, logos);
        return await action(ctx);
    }

    private async Task<Dictionary<Guid, string?>> LoadRestaurantLogoUrlsAsync(
        IReadOnlyCollection<Guid> restaurantIds,
        CancellationToken ct)
    {
        var ids = restaurantIds.Where(x => x != Guid.Empty).Distinct().ToList();
        var result = ids.ToDictionary(x => x, _ => (string?)null);
        if (ids.Count == 0) return result;

        var documents = await _db.Documents.AsNoTracking()
            .Where(x => ids.Contains(x.OwnerId)
                && x.OwnerType == DocumentOwnerType.Restaurant
                && x.DocumentType == DocumentType.ProfileImage)
            .Select(x => new { x.OwnerId, x.StoragePath })
            .ToListAsync(ct);
        var registrations = await _db.RestaurantRegistrations.AsNoTracking()
            .Where(x => x.ResultingRestaurantId != null && ids.Contains(x.ResultingRestaurantId.Value))
            .Select(x => new { x.Id, RestaurantId = x.ResultingRestaurantId!.Value })
            .ToListAsync(ct);
        var branchOwnerIds = ids.Concat(registrations.Select(x => x.Id)).Distinct().ToList();
        var branchPhotos = await _db.RestaurantBranches.AsNoTracking()
            .Where(x => branchOwnerIds.Contains(x.RestaurantRegistrationId) && x.IsActive)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.NameAr)
            .Select(x => new { x.RestaurantRegistrationId, x.PhotoStorageKey })
            .ToListAsync(ct);

        foreach (var id in ids)
        {
            var logo = documents.FirstOrDefault(x => x.OwnerId == id)?.StoragePath;
            if (string.IsNullOrWhiteSpace(logo))
            {
                var registrationId = registrations.FirstOrDefault(x => x.RestaurantId == id)?.Id;
                logo = branchPhotos.FirstOrDefault(x => x.RestaurantRegistrationId == id)?.PhotoStorageKey
                    ?? (registrationId is null
                        ? null
                        : branchPhotos.FirstOrDefault(x => x.RestaurantRegistrationId == registrationId)?.PhotoStorageKey);
            }

            result[id] = CatalogImageStorage.ToContentUrl(logo);
        }

        return result;
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
                restaurant.RestaurantId, restaurant.NameAr, restaurant.NameEn, ctx.LogoUrl(restaurant.RestaurantId), restaurant.Tier,
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

    private async Task<IReadOnlyList<CustomerCalendarDaySummaryDto>> MapDaySummariesAsync(CalendarContext ctx, CancellationToken ct)
    {
        var result = new List<CustomerCalendarDaySummaryDto>(ctx.Days.Count);
        foreach (var day in ctx.Days)
            result.Add(await MapDaySummaryAsync(ctx, day, ct));
        return result;
    }

    private Task<CustomerCalendarDaySummaryDto> MapDaySummaryAsync(CalendarContext ctx, SubscriptionDay day, CancellationToken ct)
    {
        var restaurantId = EffectiveCustomerRestaurantId(ctx, day);
        var frozen = restaurantId is { } rid
            ? ctx.Restaurants.FirstOrDefault(x => x.RestaurantId == rid)
            : null;
        var meals = CurrentDayMeals(ctx, day);
        var selected = ctx.Selections.Where(x => x.SubscriptionDayId == day.Id).OrderBy(x => x.SortOrder)
            .Select(selection =>
            {
                var meal = meals.FirstOrDefault(x => x.MealId == selection.MealId);
                return meal is null
                    ? new CustomerCalendarMealSummaryDto(
                        selection.MealId, "", "", "", "", "",
                        selection.SortOrder, null, IsSelected: true)
                    : ToMealSummaryDto(meal, isSelected: true);
            })
            .ToList();
        var filled = selected.Count;
        var remaining = day.SelectionDeadlineUtc is { } deadline
            ? (int)Math.Max(0, Math.Ceiling((deadline - _clock.UtcNow).TotalSeconds))
            : (int?)null;
        var canEdit = !LockedStatuses.Contains(day.Status)
            && (day.SelectionDeadlineUtc is null || _clock.UtcNow < day.SelectionDeadlineUtc);
        return Task.FromResult(new CustomerCalendarDaySummaryDto(
            day.Id, day.ServiceDate, restaurantId, frozen?.NameAr, frozen?.NameEn,
            restaurantId is { } logoRestaurantId ? ctx.LogoUrl(logoRestaurantId) : null,
            day.Status, MapUiStatus(day, filled, restaurantId), day.IsAutoSelected, canEdit,
            day.SelectionDeadlineUtc, day.LockAtUtc, remaining, filled, ctx.RequiredCount,
            CustomerCalendarSlotLabels.ToDtos(ctx.SlotRequirements),
            day.Status == SubscriptionDayStatus.ReplacementRequired ? MessageCodes.Calendar.NoSafeBox : null,
            selected));
    }

    private async Task<CustomerCalendarDayDto> MapDayAsync(CalendarContext ctx, SubscriptionDay day, CancellationToken ct)
    {
        var restaurantId = EffectiveCustomerRestaurantId(ctx, day);
        var frozen = restaurantId is { } rid
            ? ctx.Restaurants.FirstOrDefault(x => x.RestaurantId == rid)
            : null;
        var meals = CurrentDayMeals(ctx, day);
        var media = await LoadIngredientMediaAsync(meals.SelectMany(x => x.Ingredients.Select(i => i.Id)), ct);
        var selected = ctx.Selections.Where(x => x.SubscriptionDayId == day.Id).OrderBy(x => x.SortOrder)
            .Select(selection =>
            {
                var meal = meals.FirstOrDefault(x => x.MealId == selection.MealId);
                return meal is null
                    ? new CustomerCalendarMealDto(
                        selection.MealId, "", "", "", "", "",
                        selection.SortOrder, null, null, null, null, null, null, null, [], [], [], [], null, IsSelected: true)
                    : ToMealDto(meal, ctx.Preferences, isSelected: true, media);
            })
            .ToList();
        var filled = selected.Count;
        var remaining = day.SelectionDeadlineUtc is { } deadline
            ? (int)Math.Max(0, Math.Ceiling((deadline - _clock.UtcNow).TotalSeconds))
            : (int?)null;
        var canEdit = !LockedStatuses.Contains(day.Status)
            && (day.SelectionDeadlineUtc is null || _clock.UtcNow < day.SelectionDeadlineUtc);
        return new CustomerCalendarDayDto(
            day.Id, day.ServiceDate, restaurantId, frozen?.NameAr, frozen?.NameEn,
            restaurantId is { } logoRestaurantId ? ctx.LogoUrl(logoRestaurantId) : null,
            day.Status, MapUiStatus(day, filled, restaurantId), day.IsAutoSelected, canEdit,
            day.SelectionDeadlineUtc, day.LockAtUtc, remaining, filled, ctx.RequiredCount,
            CustomerCalendarSlotLabels.ToDtos(ctx.SlotRequirements),
            day.Status == SubscriptionDayStatus.ReplacementRequired ? MessageCodes.Calendar.NoSafeBox : null,
            SumSelectedNutrition(selected),
            selected);
    }

    private static CustomerDayNutritionTotalsDto SumSelectedNutrition(IReadOnlyList<CustomerCalendarMealDto> selected)
        => new(
            selected.Sum(x => x.Calories ?? 0m),
            selected.Sum(x => x.ProteinG ?? 0m),
            selected.Sum(x => x.CarbsG ?? 0m),
            selected.Sum(x => x.FatG ?? 0m));

    /// <summary>
    /// Restaurant shown to the customer only after an explicit pick (or auto-select / locked progress).
    /// Fulfillment may still store a hold restaurant on the day row for older subscriptions — hide that.
    /// </summary>
    private static Guid? EffectiveCustomerRestaurantId(CalendarContext ctx, SubscriptionDay day)
    {
        if (day.RestaurantId is null) return null;
        if (day.IsAutoSelected) return day.RestaurantId;
        if (day.Status != SubscriptionDayStatus.Open) return day.RestaurantId;
        if (ctx.RestaurantPicks.Any(x => x.SubscriptionDayId == day.Id)) return day.RestaurantId;
        if (ctx.Selections.Any(x => x.SubscriptionDayId == day.Id)) return day.RestaurantId;
        return null;
    }

    private static string MapUiStatus(SubscriptionDay day, int filled, Guid? restaurantId)
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
        if (filled == 0 && restaurantId is null) return CustomerCalendarUiStatus.Empty;
        return CustomerCalendarUiStatus.Drafting;
    }

    private static List<MealSnapshotItem> CurrentDayMeals(CalendarContext ctx, SubscriptionDay day)
    {
        // Prefer the customer-visible restaurant; fall back to the stored day restaurant so selected
        // meals still hydrate when revisiting a day (even if display temporarily hides a hold restaurant).
        var restaurantId = EffectiveCustomerRestaurantId(ctx, day) ?? day.RestaurantId;
        if (restaurantId is { } rid)
        {
            var restaurantDay = ctx.RestaurantDays.FirstOrDefault(x => x.RestaurantId == rid && x.ServiceDate == day.ServiceDate);
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

    private static CustomerCalendarMealSummaryDto ToMealSummaryDto(MealSnapshotItem meal, bool isSelected = false)
        => new(
            meal.MealId, meal.NameAr, meal.NameEn, meal.Slot,
            CustomerCalendarSlotLabels.NameAr(meal.Slot), CustomerCalendarSlotLabels.NameEn(meal.Slot),
            meal.SortOrder, meal.ImageUrl, isSelected);

    private async Task<IReadOnlyDictionary<Guid, IngredientMedia>> LoadIngredientMediaAsync(
        IEnumerable<Guid> ingredientIds, CancellationToken ct)
    {
        var ids = ingredientIds.Where(x => x != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, IngredientMedia>();
        return await _db.Ingredients.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.IsCore, x.MealIconUrl, x.RestaurantIconUrl })
            .ToDictionaryAsync(
                x => x.Id,
                x => new IngredientMedia(x.IsCore, x.MealIconUrl, x.RestaurantIconUrl),
                ct);
    }

    private async Task<IReadOnlyList<CustomerRestaurantCoreIngredientDto>> LoadRestaurantCoreIngredientsAsync(
        Guid restaurantId, CancellationToken ct)
    {
        var rows = await _db.Ingredients.AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId && x.IsActive && x.IsCore)
            .OrderBy(x => x.NameEn)
            .Select(x => new { x.Id, x.NameAr, x.NameEn, x.RestaurantIconUrl, x.MealIconUrl })
            .ToListAsync(ct);
        return rows
            .Select(x => new CustomerRestaurantCoreIngredientDto(
                x.Id,
                x.NameAr,
                x.NameEn,
                CatalogImageStorage.ToContentUrl(x.RestaurantIconUrl ?? x.MealIconUrl)))
            .ToList();
    }

    private static CustomerCalendarMealDto ToMealDto(
        MealSnapshotItem meal,
        MealPreferenceFilter.CustomerPreferenceState preferences,
        bool isSelected = false,
        IReadOnlyDictionary<Guid, IngredientMedia>? ingredientMedia = null)
        => new(
            meal.MealId, meal.NameAr, meal.NameEn, meal.Slot,
            CustomerCalendarSlotLabels.NameAr(meal.Slot), CustomerCalendarSlotLabels.NameEn(meal.Slot),
            meal.SortOrder, meal.ImageUrl,
            meal.Calories, meal.ProteinG, meal.CarbsG, meal.FatG, meal.DescriptionAr, meal.DescriptionEn,
            meal.PresentAllergenCodes, meal.MayContainAllergenCodes, meal.DietaryPreferenceCodes,
            // Core ingredients belong on restaurant details (coreIngredients), not on every meal chip.
            meal.Ingredients
                .Where(i => !IsCoreIngredient(i, ingredientMedia))
                .Select(i => ToIngredientDto(i, ingredientMedia))
                .ToList(),
            MealPreferenceFilter.BlockReason(meal, preferences),
            isSelected);

    private static bool IsCoreIngredient(
        MealSnapshotIngredient ingredient,
        IReadOnlyDictionary<Guid, IngredientMedia>? media)
    {
        if (media is not null && media.TryGetValue(ingredient.Id, out var live))
            return live.IsCore;
        return ingredient.IsCore;
    }

    private static CustomerMealIngredientDto ToIngredientDto(
        MealSnapshotIngredient ingredient,
        IReadOnlyDictionary<Guid, IngredientMedia>? media)
    {
        var isCore = ingredient.IsCore;
        var iconKey = ingredient.MealIconUrl;
        if (media is not null && media.TryGetValue(ingredient.Id, out var live))
        {
            isCore = live.IsCore;
            if (!string.IsNullOrWhiteSpace(live.MealIconUrl))
                iconKey = live.MealIconUrl;
        }
        return new CustomerMealIngredientDto(
            ingredient.Id,
            ingredient.NameAr,
            ingredient.NameEn,
            ingredient.IsOriginVisibleToCustomers ? ingredient.OriginCountryCode : null,
            ingredient.IsOriginVisibleToCustomers ? ingredient.SupplierName : null,
            ingredient.IsLocal,
            ingredient.IsHalalCertified,
            ingredient.AllergenCodes,
            isCore,
            CatalogImageStorage.ToContentUrl(iconKey));
    }

    private sealed record IngredientMedia(bool IsCore, string? MealIconUrl, string? RestaurantIconUrl = null);

    private sealed record PickedBox(SubscriptionRestaurantDaySnapshot Snapshot, IReadOnlyList<MealSnapshotItem> Meals);

    private sealed class CalendarContext
    {
        public CalendarContext(
            Subscription subscription,
            List<SubscriptionDay> days,
            List<DailyMealSelection> selections,
            List<DailyRestaurantSelection> restaurantPicks,
            List<SubscriptionDayMealSnapshot> daySnapshots,
            List<SubscriptionRestaurantSnapshot> restaurants,
            List<SubscriptionRestaurantDaySnapshot> restaurantDays,
            List<MealMate.Domain.RestaurantOperations.BusyStatus> busy,
            MealPreferenceFilter.CustomerPreferenceState preferences,
            int limit,
            int adminCeiling,
            IReadOnlyList<SlotRequirementDto> slotRequirements,
            IReadOnlyDictionary<Guid, string?> restaurantLogos)
        {
            Subscription = subscription;
            Days = days;
            Selections = selections;
            RestaurantPicks = restaurantPicks;
            DaySnapshots = daySnapshots;
            Restaurants = restaurants;
            RestaurantDays = restaurantDays;
            Busy = busy;
            Preferences = preferences;
            Limit = limit;
            AdminCeiling = adminCeiling;
            SlotRequirements = slotRequirements;
            RestaurantLogos = restaurantLogos;
        }

        public Subscription Subscription { get; }
        public List<SubscriptionDay> Days { get; }
        public List<DailyMealSelection> Selections { get; }
        public List<DailyRestaurantSelection> RestaurantPicks { get; }
        public List<SubscriptionDayMealSnapshot> DaySnapshots { get; }
        public List<SubscriptionRestaurantSnapshot> Restaurants { get; }
        public List<SubscriptionRestaurantDaySnapshot> RestaurantDays { get; }
        public List<MealMate.Domain.RestaurantOperations.BusyStatus> Busy { get; }
        public MealPreferenceFilter.CustomerPreferenceState Preferences { get; }
        public int Limit { get; }
        public int AdminCeiling { get; }
        public IReadOnlyList<SlotRequirementDto> SlotRequirements { get; }
        public IReadOnlyDictionary<Guid, string?> RestaurantLogos { get; }
        public int RequiredCount => SlotRequirements.Sum(x => x.Count);
        public SubscriptionDay? Day(DateOnly date) => Days.FirstOrDefault(x => x.ServiceDate == date);
        public string? LogoUrl(Guid restaurantId) => RestaurantLogos.GetValueOrDefault(restaurantId);
    }
}
