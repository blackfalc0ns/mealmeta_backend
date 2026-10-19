using System.Globalization;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions.CustomerSubscriptions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.CalendarMeals;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramNutrition;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;

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
    private readonly IMealRecommendationEngine _recommendationEngine;
    private readonly ICapacityTransferService? _capacityTransferService;
    private readonly IOperationalPolicyService? _policies;
    private readonly IHostEnvironment? _environment;
    private readonly IApplicationTransactionExecutor? _transactions;
    private readonly IApplicationLockManager? _locks;
    private readonly Features.DriverDelivery.CustomerTracking.ICustomerDeliveryReadService _deliveryRead;
    private readonly ILocalizedMessageProvider? _messages;

    public CustomerCalendarEngine(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IRestaurantDiversificationCalculator calculator,
        ISubscriptionDayDispatchService? dispatch = null,
        FamilyManager.IFamilyCalendarAccessGate? accessGate = null,
        IMealRecommendationEngine? recommendationEngine = null,
        ICapacityTransferService? capacityTransferService = null,
        IOperationalPolicyService? policies = null,
        IHostEnvironment? environment = null,
        IApplicationTransactionExecutor? transactions = null,
        IApplicationLockManager? locks = null,
        Features.DriverDelivery.CustomerTracking.ICustomerDeliveryReadService? deliveryRead = null,
        ILocalizedMessageProvider? messages = null)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _calculator = calculator;
        _dispatch = dispatch ?? NullSubscriptionDayDispatchService.Instance;
        _accessGate = accessGate ?? new FamilyManager.FamilyCalendarAccessGate(db, currentUser);
        _recommendationEngine = recommendationEngine ?? new MealRecommendationEngine();
        _capacityTransferService = capacityTransferService;
        _policies = policies;
        _environment = environment;
        _transactions = transactions;
        _locks = locks;
        _deliveryRead = deliveryRead ?? new Features.DriverDelivery.CustomerTracking.CustomerDeliveryReadService(db);
        _messages = messages;
    }

    public Task<Result<CustomerCalendarScreenDto>> GetCalendarAsync(Guid subscriptionId, CancellationToken ct)
        => WithContext(subscriptionId, requireOwner: true, requireWrite: false, ct, async ctx =>
        {
            var days = await MapDaySummariesAsync(ctx, ct);
            // Counted from the notification inbox (never from push delivery logs) for the
            // authorized customer; the boolean derives from the same count.
            var unreadCount = _currentUser.UserId.HasValue
                ? await _db.CustomerNotifications.AsNoTracking()
                    .CountAsync(x => x.UserId == _currentUser.UserId.Value && x.ReadAtUtc == null, ct)
                : 0;
            return Result.Success(new CustomerCalendarScreenDto(
                ctx.Subscription.Id,
                ctx.CanViewPlan ? ctx.Subscription.ProgramId : null,
                ctx.CanViewPlan ? ctx.Subscription.BundleId : null,
                ctx.RequiredCount,
                CustomerCalendarSlotLabels.ToDtos(ctx.SlotRequirements),
                true,
                days,
                HasUnreadNotifications: unreadCount > 0,
                UnreadNotificationsCount: unreadCount));
        });

    public Task<Result<CustomerCalendarDayDto>> GetDayAsync(Guid subscriptionId, DateOnly date, CancellationToken ct)
        => WithContext(subscriptionId, requireOwner: true, requireWrite: false, ct, async ctx =>
        {
            var day = ctx.Day(date);
            if (day is null) return Error.NotFound(MessageCodes.Calendar.DayUnavailable);
            return Result.Success(await MapDayAsync(ctx, day, ct));
        });

    public Task<Result<CustomerDayRestaurantsDto>> GetDayRestaurantsAsync(Guid subscriptionId, DateOnly date, string? query, CancellationToken ct)
        => GetDayRestaurantsAsync(subscriptionId, date, query, null, null, ct);

    public Task<Result<CustomerDayRestaurantsDto>> GetDayRestaurantsAsync(
        Guid subscriptionId, DateOnly date, string? query, int? page, int? pageSize, CancellationToken ct)
        => WithContext(subscriptionId, requireOwner: true, requireWrite: false, ct, async ctx =>
        {
            var day = ctx.Day(date);
            if (day is null) return Error.NotFound(MessageCodes.Calendar.DayUnavailable);
            var eligible = ListRestaurants(ctx, day);
            var filtered = string.IsNullOrWhiteSpace(query)
                ? eligible
                : eligible.Where(x => MatchesSearch(x, query.Trim())).ToList();
            // Eligibility (quota/busy/allergy) was decided on the full region-covered set inside
            // ListRestaurants, so search and paging only slice the view and never re-enable a
            // quota-exhausted or busy restaurant.
            var request = new PageRequest { Page = page ?? 1, PageSize = pageSize ?? 20 };
            var isPaged = page.HasValue || pageSize.HasValue;
            var totalCount = filtered.Count;
            var size = isPaged ? request.NormalizedPageSize : Math.Max(1, totalCount);
            var number = isPaged ? request.NormalizedPage : 1;
            var visible = isPaged ? filtered.Skip(request.Skip).Take(size).ToList() : filtered;
            var totalPages = (int)Math.Ceiling(totalCount / (double)size);
            return Result.Success(new CustomerDayRestaurantsDto(
                date, ctx.Limit, ctx.AdminCeiling, visible,
                Page: number,
                PageSize: size,
                TotalCount: totalCount,
                TotalPages: totalPages,
                HasNextPage: number < totalPages,
                HasPreviousPage: number > 1));
        });

    public Task<Result<CustomerDayRestaurantMealsDto>> GetRestaurantMealsAsync(
        Guid subscriptionId, DateOnly date, Guid restaurantId, string? slot, int page, int pageSize, CancellationToken ct)
        => WithContext(subscriptionId, requireOwner: true, requireWrite: false, ct, async ctx =>
            await MapRestaurantMealsAsync(ctx, date, restaurantId, slot, page, pageSize, ct));

    /// <summary>
    /// Compatibility for Flutter clients still calling GET .../days/{date}/meals/available.
    /// Uses the day's selected restaurant, or the first restaurant in the frozen pool.
    /// </summary>
    public Task<Result<CustomerDayRestaurantMealsDto>> GetAvailableMealsAsync(
        Guid subscriptionId, DateOnly date, string? slot, int page, int pageSize, CancellationToken ct)
        => WithContext(subscriptionId, requireOwner: true, requireWrite: false, ct, async ctx =>
        {
            var day = ctx.Day(date);
            if (day is null) return Error.NotFound(MessageCodes.Calendar.DayUnavailable);
            var restaurantId = day.RestaurantId
                ?? ctx.Restaurants.OrderByDescending(x => x.IsInitiallySelected).Select(x => (Guid?)x.RestaurantId).FirstOrDefault();
            if (restaurantId is null)
                return Error.Validation(MessageCodes.Calendar.RestaurantNotInPool);
            return await MapRestaurantMealsAsync(ctx, date, restaurantId.Value, slot, page, pageSize, ct);
        });

    private async Task<Result<CustomerDayRestaurantMealsDto>> MapRestaurantMealsAsync(
        CalendarContext ctx, DateOnly date, Guid restaurantId, string? slot, int page, int pageSize, CancellationToken ct)
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
        var allMeals = parsed
            .Select(x => ToMealDto(x, ctx.Preferences, selectedIds.Contains(x.MealId), media, ctx.NutritionTarget))
            .OrderBy(x => x.SortOrder)
            .ToList();

        var pageReq = new PageRequest { Page = page, PageSize = pageSize };
        var targetSlots = ctx.SlotRequirements
            .Where(x => slot is null || x.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var slotDtos = new List<CustomerCalendarSlotMealsDto>(targetSlots.Count);
        foreach (var req in targetSlots)
        {
            var slotMeals = allMeals
                .Where(m => m.Slot.Equals(req.Slot, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var totalCount = slotMeals.Count;
            var totalPages = pageReq.NormalizedPageSize == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageReq.NormalizedPageSize);
            var pagedMeals = slotMeals
                .Skip(pageReq.Skip)
                .Take(pageReq.NormalizedPageSize)
                .ToList();

            slotDtos.Add(new CustomerCalendarSlotMealsDto(
                Slot: req.Slot,
                SlotNameAr: CustomerCalendarSlotLabels.NameAr(req.Slot),
                SlotNameEn: CustomerCalendarSlotLabels.NameEn(req.Slot),
                Count: req.Count,
                Page: pageReq.NormalizedPage,
                PageSize: pageReq.NormalizedPageSize,
                TotalCount: totalCount,
                TotalPages: totalPages,
                HasNextPage: pageReq.NormalizedPage < totalPages,
                HasPreviousPage: pageReq.NormalizedPage > 1,
                Meals: pagedMeals));
        }

        var coreIngredients = await LoadRestaurantCoreIngredientsAsync(restaurantId, ct);
        var filled = (EffectiveCustomerRestaurantId(ctx, day) ?? day.RestaurantId) == restaurantId
            ? CountFilled(ctx, day)
            : 0;
        var (dwStart, dwEnd) = ctx.DeliveryWindow(restaurantId, day.DeliveryRegionId);
        var dayMeals = CurrentDayMeals(ctx, day);
        var daySelected = ctx.Selections.Where(x => x.SubscriptionDayId == day.Id).OrderBy(x => x.SortOrder)
            .Select(selection =>
            {
                var snapshot = dayMeals.FirstOrDefault(x => x.MealId == selection.MealId);
                if (snapshot is not null)
                {
                    return ToMealDto(snapshot, ctx.Preferences, isSelected: true, media, ctx.NutritionTarget);
                }

                var existing = allMeals.FirstOrDefault(x => x.MealId == selection.MealId);
                if (existing is not null)
                {
                    return existing with { IsSelected = true };
                }

                return new CustomerCalendarMealDto(
                    selection.MealId, "", "", "", "", "",
                    selection.SortOrder, null, null, null, null, null, null, null, [], [], [], [], null, IsSelected: true);
            })
            .ToList();
        var dayNutrition = SumSelectedNutrition(daySelected, ctx.NutritionTarget);
        var tierInfo = ctx.TierInfo(restaurantId);
        var programInfo = ctx.ProgramInfo();

        return Result.Success(new CustomerDayRestaurantMealsDto(
            RestaurantId: restaurantId,
            NameAr: frozen.NameAr,
            NameEn: frozen.NameEn,
            LogoUrl: ctx.LogoUrl(restaurantId),
            ServiceDate: date,
            RequiredCount: ctx.RequiredCount,
            FilledCount: filled,
            Slots: slotDtos,
            CoreIngredients: coreIngredients,
            SelectedMealIds: selectedIds.ToList(),
            Rating: ctx.RestaurantRating(restaurantId),
            RatingCount: ctx.RestaurantRatingCount(restaurantId),
            DeliveryWindowStart: dwStart,
            DeliveryWindowEnd: dwEnd,
            DeliveryTimeText: FormatDeliveryTimeText(dwStart, dwEnd),
            Tier: tierInfo?.Tier,
            TierNameAr: tierInfo?.TierNameAr,
            TierNameEn: tierInfo?.TierNameEn,
            TierName: tierInfo?.TierName,
            TierLogoUrl: tierInfo?.TierLogoUrl,
            ProgramNameAr: programInfo?.ProgramNameAr,
            ProgramNameEn: programInfo?.ProgramNameEn,
            ProgramName: programInfo?.ProgramName,
            NutritionTotals: dayNutrition,
            CaloriesPercentage: dayNutrition.CaloriesPercentage,
            ProteinPercentage: dayNutrition.ProteinPercentage,
            CarbsPercentage: dayNutrition.CarbsPercentage,
            FatPercentage: dayNutrition.FatPercentage));
    }

    public Task<Result<CustomerCalendarDayDto>> SelectRestaurantAsync(Guid subscriptionId, DateOnly date, Guid restaurantId, CancellationToken ct)
        => WithContext<CustomerCalendarDayDto>(subscriptionId, requireOwner: true, requireWrite: true, ct, FamilyAction.SelectRestaurant, async ctx =>
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
                if (_capacityTransferService is not null)
                {
                    var transfer = await _capacityTransferService.TransferCapacityAsync(
                        subscriptionId, day.Id, date, day.RestaurantId, restaurantId, quantity: 1, ct);
                    if (transfer.IsFailure)
                        return transfer.Error;
                }

                var snapshot = ctx.RestaurantDays.FirstOrDefault(x => x.RestaurantId == restaurantId && x.ServiceDate == date);
                if (snapshot is null) return Error.Conflict(MessageCodes.Calendar.BoxNotReady);
                // Keep the dispatched order attached to the customer's current restaurant.
                // The day becomes a draft until the new box is complete and sent again.
                var pendingOrders = await _db.RestaurantDailyOrders
                    .Where(x => x.SubscriptionDayId == day.Id && x.Status == OrderStatus.AwaitingRestaurantConfirmation)
                    .ToListAsync(ct);
                foreach (var order in pendingOrders)
                {
                    order.RestaurantId = restaurantId;
                    order.ModifiedAtUtc = _clock.UtcNow;
                }
                ApplyRestaurantSnapshot(ctx, day, snapshot);
                var meals = await _db.DailyMealSelections.IgnoreQueryFilters()
                    .Where(x => x.SubscriptionDayId == day.Id)
                    .ToListAsync(ct);
                await HardDeleteMealSelectionsAsync(meals, ct);
                ctx.Selections.RemoveAll(x => x.SubscriptionDayId == day.Id);
                day.Status = SubscriptionDayStatus.Open;
                // This change passed the editability gate. The reopened draft must permit
                // refilling while preventing restaurant confirmation of the empty box.
                day.LockAtUtc = null;
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
        => WithContext(subscriptionId, requireOwner: true, requireWrite: true, ct, FamilyAction.ViewCalendar, async ctx =>
        {
            var day = ctx.Day(date);
            if (day is null) return Error.NotFound(MessageCodes.Calendar.DayUnavailable);
            var legacyEdit = await _accessGate.AuthorizeAsync(ctx.Subscription, FamilyAction.EditDailyMeals, ct);
            var addMeal = await _accessGate.AuthorizeAsync(ctx.Subscription, FamilyAction.EditMealsAndDays, ct);
            var exchangeMeal = await _accessGate.AuthorizeAsync(ctx.Subscription, FamilyAction.UnitExchange, ct);
            var edit = EnsureEditable(ctx, day, allowAfterDeadline: false);
            if (edit.IsFailure) return edit.Error;

            // Auto-resolve restaurant from meal if not yet selected or if day has no restaurant assigned
            var matchingRestaurantDay = ctx.RestaurantDays.FirstOrDefault(rd =>
                rd.ServiceDate == date && ParseMeals(rd.MealsJson).Any(m => m.MealId == mealId));
            if (matchingRestaurantDay is not null)
            {
                if (day.RestaurantId is null || (day.RestaurantId != matchingRestaurantDay.RestaurantId && !ctx.Selections.Any(x => x.SubscriptionDayId == day.Id)))
                {
                    day.RestaurantId = matchingRestaurantDay.RestaurantId;
                    ApplyRestaurantSnapshot(ctx, day, matchingRestaurantDay);
                }

                if (day.RestaurantId == matchingRestaurantDay.RestaurantId && !ctx.RestaurantPicks.Any(x => x.SubscriptionDayId == day.Id))
                {
                    var pick = new DailyRestaurantSelection
                    {
                        SubscriptionDayId = day.Id,
                        RestaurantId = matchingRestaurantDay.RestaurantId,
                        SelectedByUserId = _currentUser.UserId,
                        SelectedAtUtc = _clock.UtcNow,
                        SelectionSource = "Manual"
                    };
                    _db.DailyRestaurantSelections.Add(pick);
                    ctx.RestaurantPicks.Add(pick);
                }
            }
            else if (day.RestaurantId.HasValue && !ctx.RestaurantPicks.Any(x => x.SubscriptionDayId == day.Id))
            {
                var pick = new DailyRestaurantSelection
                {
                    SubscriptionDayId = day.Id,
                    RestaurantId = day.RestaurantId.Value,
                    SelectedByUserId = _currentUser.UserId,
                    SelectedAtUtc = _clock.UtcNow,
                    SelectionSource = "Manual"
                };
                _db.DailyRestaurantSelections.Add(pick);
                ctx.RestaurantPicks.Add(pick);
            }

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
            var requiredGrant = slotMeals.Count >= requirement.Count ? exchangeMeal : addMeal;
            if (requiredGrant.IsFailure && legacyEdit.IsFailure)
                return requiredGrant.Error;
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

            if (day.Status != SubscriptionDayStatus.SentToRestaurant)
                day.Status = SubscriptionDayStatus.Open;
            else
            {
                // Include the day concurrency token when saving meal changes so a concurrent
                // restaurant confirmation cannot leave a confirmed box with later customer edits.
                day.ModifiedAtUtc = _clock.UtcNow;
                if (_db is DbContext ef)
                    ef.Entry(day).Property(x => x.Status).IsModified = true;
            }
            day.IsAutoSelected = false;
            return Result.Success(await MapDayAsync(ctx, day, ct));
        });

    public Task<Result<CustomerCalendarDayDto>> CompleteBoxAsync(Guid subscriptionId, DateOnly date, string? idempotencyKey, CancellationToken ct)
        => WithContext(subscriptionId, requireOwner: true, requireWrite: true, ct, FamilyAction.ViewCalendar, async ctx =>
        {
            var day = ctx.Day(date);
            if (day is null) return Error.NotFound(MessageCodes.Calendar.DayUnavailable);
            var editPermission = await _accessGate.AuthorizeAsync(ctx.Subscription, FamilyAction.EditMealsAndDays, ct);
            var legacyEdit = await _accessGate.AuthorizeAsync(ctx.Subscription, FamilyAction.EditDailyMeals, ct);
            if (editPermission.IsFailure && legacyEdit.IsFailure) return editPermission.Error;
            if ((day.Status is SubscriptionDayStatus.CustomerSelected or SubscriptionDayStatus.SentToRestaurant) && IsBoxComplete(ctx, day))
                return Result.Success(await MapDayAsync(ctx, day, ct));
            var edit = EnsureEditable(ctx, day, allowAfterDeadline: false);
            if (edit.IsFailure) return edit.Error;
            if (!IsBoxComplete(ctx, day))
                return Error.Validation(MessageCodes.Calendar.BoxIncomplete);

            var eligibility = CurrentRestaurantEligibility(ctx, day);
            if (eligibility.IsFailure && eligibility.Error.Code != MessageCodes.Calendar.AllergyUncovered)
                return eligibility.Error;
            day.Status = SubscriptionDayStatus.CustomerSelected;
            day.IsAutoSelected = false;
            return Result.Success(await MapDayAsync(ctx, day, ct));
        });

    public Task<Result<CustomerCalendarDayDto>> ConfirmAndSendToRestaurantAsync(Guid subscriptionId, DateOnly date, CancellationToken ct)
        => WithContext(subscriptionId, requireOwner: true, requireWrite: true, ct, FamilyAction.ConfirmMeals, async ctx =>
        {
            var day = ctx.Day(date);
            if (day is null) return Error.NotFound(MessageCodes.Calendar.DayUnavailable);

            if (day.Status is SubscriptionDayStatus.SentToRestaurant
                or SubscriptionDayStatus.ConfirmedByRestaurant
                or SubscriptionDayStatus.Preparing
                or SubscriptionDayStatus.OutForDelivery
                or SubscriptionDayStatus.Delivered)
                return Result.Success(await MapDayAsync(ctx, day, ct));

            if (day.Status is not SubscriptionDayStatus.CustomerSelected
                and not SubscriptionDayStatus.AutoSelected)
            {
                var edit = EnsureEditable(ctx, day, allowAfterDeadline: false);
                if (edit.IsFailure) return edit.Error;
            }

            if (!IsBoxComplete(ctx, day))
                return Error.Validation(MessageCodes.Calendar.BoxIncomplete);

            var eligibility = CurrentRestaurantEligibility(ctx, day);
            if (eligibility.IsFailure && eligibility.Error.Code != MessageCodes.Calendar.AllergyUncovered)
                return eligibility.Error;
            // Re-check saved meals against the customer's current allergy preferences.
            var safety = await EnsureBoxSafeForSendAsync(ctx, day, ct);
            if (safety.IsFailure)
            {
                ctx.UnsafeBoxFailureDayId = day.Id;
                return Error.Conflict(MessageCodes.Calendar.NoSafeBox);
            }

            day.IsAutoSelected = false;
            // Customer confirmation saves the box; dispatch only when the configured cutoff is due.
            // A reopened dispatched box updates its existing pending order after completion.
            var hasPendingOrder = await _db.RestaurantDailyOrders.AnyAsync(
                x => x.SubscriptionDayId == day.Id && x.Status == OrderStatus.AwaitingRestaurantConfirmation, ct);
            if (!hasPendingOrder && day.LockAtUtc is null && (day.GetEffectiveSelectionDeadlineUtc() is null || _clock.UtcNow < day.GetEffectiveSelectionDeadlineUtc()))
            {
                day.Status = SubscriptionDayStatus.CustomerSelected;
                return Result.Success(await MapDayAsync(ctx, day, ct));
            }

            var sent = await _dispatch.LockAndSendToRestaurantAsync(day, ctx.Subscription.Id, ct);
            if (sent.IsFailure) return sent.Error;
            return Result.Success(await MapDayAsync(ctx, day, ct));
        });

    public Task<Result<CustomerCalendarDayDto>> AutoSelectFullBoxAsync(
        Guid subscriptionId, DateOnly date, bool lockAfter, bool requireOwner, CancellationToken ct, bool scheduledAutomatic = false)
        => WithContext(subscriptionId, requireOwner, requireWrite: true, ct, async ctx =>
        {
            var day = ctx.Day(date);
            if (day is null) return Error.NotFound(MessageCodes.Calendar.DayUnavailable);
            if (day.Status is SubscriptionDayStatus.Cancelled or SubscriptionDayStatus.Frozen)
                return Error.Conflict(MessageCodes.Calendar.DayUnavailable);
            if (ctx.Subscription.FamilyGroupId.HasValue
                && ctx.Subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled or SubscriptionStatus.Frozen)
                && OperationalDeadlineRules.IsMutable(day))
                return Error.Conflict("subscription.service_unavailable");

            var pastDeadline = day.GetEffectiveSelectionDeadlineUtc() is { } deadline && _clock.UtcNow >= deadline;
            var currentPolicies = await OperationalPolicyRules.ReadAsync(_db, ct);
            if (scheduledAutomatic && (currentPolicies.AutoSelection?.Enabled == false || day.GetEffectiveSelectionDeadlineUtc() is not { } automaticDeadline ||
                _clock.UtcNow < automaticDeadline.AddMinutes(-(currentPolicies.AutoSelection?.MinutesBeforeCutoff ?? 0))))
                return Error.Conflict("subscription.auto_selection_not_due", "Scheduled automatic selection is disabled or not due.");
            if (lockAfter && !pastDeadline && OperationalDeadlineRules.IsMutable(day))
                return Error.Conflict("subscription.cutoff_not_due", "The selection deadline has not passed.");
            if (!lockAfter)
            {
                var edit = EnsureEditable(ctx, day, allowAfterDeadline: false);
                if (edit.IsFailure) return edit.Error;
            }
            else if (LockedStatuses.Contains(day.Status) && day.Status != SubscriptionDayStatus.Locked72h)
                return Error.Conflict(MessageCodes.Calendar.DayUnavailable);

            var savedEligibility = CurrentRestaurantEligibility(ctx, day);
            if ((savedEligibility.IsSuccess || savedEligibility.Error.Code == MessageCodes.Calendar.AllergyUncovered) && IsBoxComplete(ctx, day) &&
                (day.Status == SubscriptionDayStatus.CustomerSelected || day.Status == SubscriptionDayStatus.AutoSelected || lockAfter))
            {
                // A complete saved box is never dispatched without re-checking the customer's
                // current allergy state; an unrepairable conflict follows the NoSafeBox path.
                var safety = await EnsureBoxSafeForSendAsync(ctx, day, ct);
                if (safety.IsFailure)
                {
                    ctx.UnsafeBoxFailureDayId = day.Id;
                    return Error.Conflict(MessageCodes.Calendar.NoSafeBox);
                }
                if (lockAfter)
                {
                    var sent = await _dispatch.LockAndSendToRestaurantAsync(day, ctx.Subscription.Id, ct);
                    if (sent.IsFailure) return sent.Error;
                }
                return Result.Success(await MapDayAsync(ctx, day, ct));
            }

            if (!requireOwner && currentPolicies.AutoSelection?.Enabled == false)
            {
                // Disabling generation must not hide an invalid saved box's actual failure.
                if (IsBoxComplete(ctx, day) && savedEligibility.IsFailure) return savedEligibility.Error;
                return Error.Conflict("subscription.auto_selection_disabled", "Automatic selection is disabled. Complete customer selections are required before dispatch.");
            }

            // Capacity is checked under its reservation locks, after recommendation. A rejected
            // restaurant must not prevent another safe date-specific box from being considered.
            if (requireOwner && ctx.Subscription.FamilyGroupId.HasValue && EffectiveCustomerRestaurantId(ctx, day) is null)
            {
                var restaurantAccess = await _accessGate.AuthorizeAsync(ctx.Subscription, FamilyAction.SelectRestaurant, ct);
                if (restaurantAccess.IsFailure) return restaurantAccess.Error;
            }

            var previousRestaurantId = await ResolveHoldRestaurantAsync(ctx, day, ct) ?? day.RestaurantId;
            var rejectedRestaurants = new HashSet<Guid>();
            Error? capacityError = null;
            PickedBox? picked;
            while (true)
            {
                picked = TryPickFullBox(ctx, day, rejectedRestaurants);
                if (picked is null)
                {
                    if (capacityError is not null) return capacityError;
                    day.Status = SubscriptionDayStatus.ReplacementRequired;
                    day.LockAtUtc = null;
                    return Error.Conflict(UnavailableBoxReason(ctx, day));
                }

                if (requireOwner && EffectiveCustomerRestaurantId(ctx, day) != picked.Snapshot.RestaurantId)
                {
                    var restaurantAccess = await _accessGate.AuthorizeAsync(ctx.Subscription, FamilyAction.SelectRestaurant, ct);
                    if (restaurantAccess.IsFailure) return restaurantAccess.Error;
                }
                if (picked.Snapshot.RestaurantId == previousRestaurantId) break;
                if (_capacityTransferService is null)
                    return Error.Conflict("calendar.capacity_transfer_unavailable", "Capacity transfer service is not configured; the restaurant cannot be changed automatically.");
                var transfer = await _capacityTransferService.TransferCapacityAsync(
                    subscriptionId, day.Id, date, previousRestaurantId, picked.Snapshot.RestaurantId, quantity: 1, ct);
                if (transfer.IsSuccess) break;
                if (transfer.Error.Code != "RESTAURANT_CAPACITY_FULL") return transfer.Error;
                capacityError = transfer.Error;
                rejectedRestaurants.Add(picked.Snapshot.RestaurantId);
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
            day.FailureReasonCode = null;
            if (lockAfter || pastDeadline)
            {
                var sent = await _dispatch.LockAndSendToRestaurantAsync(day, ctx.Subscription.Id, ct);
                if (sent.IsFailure) return sent.Error;
            }
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

    private async Task<Guid?> ResolveHoldRestaurantAsync(CalendarContext ctx, SubscriptionDay day, CancellationToken ct)
        => await _db.CapacityHolds
            .Where(x => x.SubscriptionId == ctx.Subscription.Id
                && x.ServiceDate == day.ServiceDate
                && x.Status != CapacityHoldStatus.Released
                && x.Status != CapacityHoldStatus.Expired)
            .Select(x => (Guid?)x.RestaurantId)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Last safety gate before a complete box is saved or dispatched: every selected meal is
    /// re-read from the current day snapshot and checked against the customer's CURRENT allergy
    /// state. A saved meal that became unsafe is replaced with a safe alternative from the same
    /// day restaurant (safe selections are kept); when no safe box can be completed the caller
    /// gets a NoSafeBox failure and no kitchen order is created.
    /// </summary>
    private async Task<Result> EnsureBoxSafeForSendAsync(CalendarContext ctx, SubscriptionDay day, CancellationToken ct)
    {
        var meals = CurrentDayMeals(ctx, day);
        var conflicts = new List<(DailyMealSelection Selection, string Slot)>();
        foreach (var selection in ctx.Selections.Where(x => x.SubscriptionDayId == day.Id).ToList())
        {
            var meal = meals.FirstOrDefault(x => x.MealId == selection.MealId);
            // A selection that no longer exists in the current snapshot is not a verified meal.
            if (meal is null) return Result.Failure(Error.Conflict(MessageCodes.Calendar.NoSafeBox));
            if (MealPreferenceFilter.BlockReason(meal, ctx.Preferences) == MealPreferenceFilter.AllergyConflict)
                conflicts.Add((selection, meal.Slot));
        }

        foreach (var (selection, slot) in conflicts)
        {
            var selectedIds = ctx.Selections.Where(x => x.SubscriptionDayId == day.Id).Select(x => x.MealId).ToHashSet();
            var replacement = meals
                .Where(x => x.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase)
                    && x.MealId != selection.MealId
                    && !selectedIds.Contains(x.MealId)
                    && MealPreferenceFilter.BlockReason(x, ctx.Preferences) != MealPreferenceFilter.AllergyConflict)
                .OrderBy(x => MealPreferenceFilter.BlockReason(x, ctx.Preferences) == MealPreferenceFilter.DietaryPreferenceConflict ? 1 : 0)
                .ThenBy(x => MealPreferenceFilter.BlockReason(x, ctx.Preferences) == MealPreferenceFilter.IngredientPreferenceConflict ? 1 : 0)
                .ThenBy(x => x.SortOrder)
                .FirstOrDefault();
            if (replacement is null)
                return Result.Failure(Error.Conflict(MessageCodes.Calendar.NoSafeBox));

            await HardDeleteMealSelectionsAsync([selection], ct);
            ctx.Selections.Remove(selection);
            day.ModifiedAtUtc = _clock.UtcNow;
            await AddMealSelectionAsync(ctx, day, replacement.MealId, selection.SortOrder, ct);
        }

        return Result.Success();
    }

    private async Task AddMealSelectionAsync(
        CalendarContext ctx, SubscriptionDay day, Guid mealId, int sortOrder, CancellationToken ct)
    {
        // Soft-deleted rows still occupy the unique (day, meal) index; revive instead of insert.
        var tombstone = await _db.DailyMealSelections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.SubscriptionDayId == day.Id && x.MealId == mealId, ct);
        if (tombstone is not null)
        {
            tombstone.IsDeleted = false;
            tombstone.DeletedAtUtc = null;
            tombstone.DeletedByUserId = null;
            tombstone.SortOrder = sortOrder;
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
                SortOrder = sortOrder,
                SelectedAtUtc = _clock.UtcNow,
                ResolvedMenuPlanVersionId = day.ResolvedMenuPlanVersionId,
                ResolvedProgramId = ctx.Subscription.ProgramId
            };
            _db.DailyMealSelections.Add(selection);
            ctx.Selections.Add(selection);
        }
    }

    private async Task<Result<T>> WithContext<T>(
        Guid subscriptionId, bool requireOwner, bool requireWrite, CancellationToken ct,
        Func<CalendarContext, Task<Result<T>>> action)
        => await WithContext(subscriptionId, requireOwner, requireWrite, ct,
            requireWrite ? FamilyAction.EditDailyMeals : FamilyAction.ViewCalendar, action);

    private async Task<Result<T>> WithContext<T>(
        Guid subscriptionId,
        bool requireOwner,
        bool requireWrite,
        CancellationToken ct,
        FamilyAction requiredAction,
        Func<CalendarContext, Task<Result<T>>> action)
    {
        if (requireWrite && _transactions is not null)
        {
            // The outer result controls the database commit; its value preserves the API result.
            // Only an explicitly requested unsafe-box transition can commit a failed API result.
            var transactionResult = await _transactions.ExecuteResultAsync<Result<T>>(async token =>
            {
                if (_locks is not null)
                {
                    var groupId = await _db.Subscriptions.AsNoTracking().Where(x => x.Id == subscriptionId).Select(x => x.FamilyGroupId).SingleOrDefaultAsync(token);
                    if (groupId.HasValue) await _locks.LockFamilyGroupAsync(groupId.Value, token);
                    await _locks.LockSubscriptionAsync(subscriptionId, token);
                }
                var transaction = (_db as DbContext)?.Database.CurrentTransaction;
                var savepoint = $"calendar_write_{Guid.NewGuid():N}";
                if (transaction is not null) await transaction.CreateSavepointAsync(savepoint, token);
                var persistedFailure = false;
                var result = await WithContextCore(subscriptionId, requireOwner, requireWrite, requiredAction, token, action, async dayId =>
                {
                    // Safety repair can delete earlier meals before a later slot fails. Undo
                    // every operation mutation while retaining the subscription lock, acquired
                    // before this savepoint, then save only the failure state on a fresh row.
                    if (transaction is not null) await transaction.RollbackToSavepointAsync(savepoint, token);
                    await PersistUnsafeBoxFailureAsync(dayId, token);
                    persistedFailure = true;
                });
                if (transaction is not null) await transaction.ReleaseSavepointAsync(savepoint, token);
                return result.IsSuccess || persistedFailure
                    ? Result.Success(result)
                    : Result.Failure<Result<T>>(result.Error);
            }, ct);
            return transactionResult.IsSuccess ? transactionResult.Value : transactionResult.Error;
        }
        return await WithContextCore(subscriptionId, requireOwner, requireWrite, requiredAction, ct, action);
    }

    private async Task PersistUnsafeBoxFailureAsync(Guid dayId, CancellationToken ct)
    {
        if (_db is DbContext context) context.ChangeTracker.Clear();
        var day = await _db.SubscriptionDays.SingleAsync(x => x.Id == dayId, ct);
        day.Status = SubscriptionDayStatus.ReplacementRequired;
        day.FailureReasonCode = MessageCodes.Calendar.NoSafeBox;
        day.LockAtUtc = null;
        await _db.SaveChangesAsync(ct);
    }

    // A nested transaction owner must preserve this deliberate transition too. The marker
    // cannot be inferred from the error code: other NoSafeBox failures still require rollback.
    internal static bool HasPersistedUnsafeBoxFailure<T>(Result<T> result)
        => result is PersistedUnsafeBoxFailure<T>;

    private sealed class PersistedUnsafeBoxFailure<T>(Error error) : Result<T>(default, false, error);

    private async Task<Result<T>> WithContextCore<T>(Guid subscriptionId, bool requireOwner, bool requireWrite, FamilyAction requiredAction,
        CancellationToken ct, Func<CalendarContext, Task<Result<T>>> action,
        Func<Guid, Task>? persistUnsafeBoxFailure = null)
    {
        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == subscriptionId, ct);
        if (subscription is null) return Error.NotFound(MessageCodes.Common.NotFound);

        var canEditMeals = true;
        var canViewPlan = true;
        if (requireOwner)
        {
            var auth = await _accessGate.AuthorizeAsync(subscription, requiredAction, ct);
            if (auth.IsFailure) return auth.Error;
            canEditMeals = auth.Value.CanEditDailyMeals;
            if (subscription.FamilyGroupId.HasValue)
                canViewPlan = (await _accessGate.AuthorizeAsync(subscription, FamilyAction.ViewPlan, ct)).IsSuccess;
        }

        var serviceUnavailable = subscription.FamilyGroupId.HasValue
            && subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled or SubscriptionStatus.Frozen);
        if (requireOwner && requireWrite && serviceUnavailable)
            return Error.Conflict("subscription.service_unavailable");
        if (requireOwner && serviceUnavailable) canEditMeals = false;

        if (subscription.FulfillmentCompletedAtUtc is null)
            return Error.Conflict(MessageCodes.Calendar.FulfillmentNotReady);

        var days = await _db.SubscriptionDays.Where(x => x.SubscriptionId == subscriptionId).OrderBy(x => x.ServiceDate).ToListAsync(ct);
        if (subscription.FamilyGroupId.HasValue)
        {
            foreach (var day in days)
            {
                var delivery = await FamilySubscriptionDeliveryAmendments.ResolveAsync(_db, subscription, day, ct);
                if (delivery.IsFailure) return delivery.Error;
                FamilySubscriptionDeliveryAmendments.Apply(day, delivery.Value);
            }
        }
        if (_policies is not null) await OperationalDeadlineRules.RefreshAsync(_db, days, _policies, _environment?.IsDevelopment() == true || _environment?.IsEnvironment("Test") == true, ct);
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
        var serviceDayCount = days.Count(x => x.Status is not (SubscriptionDayStatus.Cancelled or SubscriptionDayStatus.Frozen));
        var limit = _calculator.CalculateLimit(subscription.Id, serviceDayCount, Math.Max(1, restaurants.Count), adminCeiling);
        var slotRequirements = SlotRequirementJson.Parse(
            restaurantDays.FirstOrDefault()?.SlotRequirementsJson
            ?? daySnapshots.FirstOrDefault()?.RequiredSlotsJson);
        var bundle = await _db.Bundles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == subscription.BundleId, ct);
        if (slotRequirements.Count == 0 && bundle is not null)
        {
            slotRequirements = new[]
            {
                new SlotRequirementDto("breakfast", bundle.BreakfastCount),
                new SlotRequirementDto("lunch", bundle.LunchCount),
                new SlotRequirementDto("dinner", bundle.DinnerCount),
                new SlotRequirementDto("snack", bundle.SnackCount)
            }.Where(x => x.Count > 0).ToList();
        }

        var poolRestaurantIds = restaurants.Select(x => x.RestaurantId).Where(x => x != Guid.Empty).Distinct().ToList();
        var ratingRows = poolRestaurantIds.Count == 0
            ? []
            : await _db.RestaurantRatings.AsNoTracking()
                .Where(x => poolRestaurantIds.Contains(x.RestaurantId))
                .GroupBy(x => x.RestaurantId)
                .Select(g => new
                {
                    RestaurantId = g.Key,
                    AvgRating = Math.Round((decimal)g.Average(r => r.Score), 1),
                    Count = g.Count()
                })
                .ToListAsync(ct);
        var ratingsMap = ratingRows.ToDictionary(x => x.RestaurantId, x => (Rating: (decimal?)x.AvgRating, Count: x.Count));

        var serviceRegions = poolRestaurantIds.Count == 0
            ? []
            : await _db.RestaurantServiceRegions.AsNoTracking()
                .Where(x => poolRestaurantIds.Contains(x.RestaurantId) && x.IsActive)
                .ToListAsync(ct);

        var logos = await LoadRestaurantLogoUrlsAsync(poolRestaurantIds, ct);
        var program = await _db.Programs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == subscription.ProgramId, ct);
        var settingsRow = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, ct);
        var tierLabels = SubscriptionPricingSettingsReader.Deserialize(settingsRow?.ValueJson).TierLabels;

        ProgramDailyNutritionTargetDto? nutritionTarget = null;
        if (program is not null && bundle is not null)
        {
            var standards = await _db.ProgramNutritionStandards.AsNoTracking()
                .Where(x => x.ProgramId == subscription.ProgramId && x.IsActive)
                .ToListAsync(ct);
            nutritionTarget = ProgramNutritionHandlers.ComputeDailyTarget(program, bundle, standards);
        }

        var rejectedMealIds = new HashSet<Guid>();
        if (dayIds.Count > 0)
        {
            var rejectedMeals = await _db.MealChangeRequests.AsNoTracking()
                .Where(x => dayIds.Contains(x.SubscriptionDayId) && x.FromMealId.HasValue)
                .Select(x => x.FromMealId!.Value)
                .ToListAsync(ct);
            foreach (var rId in rejectedMeals)
            {
                rejectedMealIds.Add(rId);
            }
        }

        var ctx = new CalendarContext(
            subscription, days, selections, restaurantPicks, daySnapshots, restaurants, restaurantDays, busy, preferences,
            limit.LimitValue, limit.LMax, slotRequirements, logos, ratingsMap, serviceRegions, program, tierLabels,
            nutritionTarget, rejectedMealIds);
        ctx.CanEditDailyMeals = canEditMeals;
        ctx.CanViewPlan = canViewPlan;
        ctx.EditPermissionReason = serviceUnavailable ? "subscription.service_unavailable" : "family.permission_required";
        var result = await action(ctx);
        if (requireWrite && result.IsSuccess) await _db.SaveChangesAsync(ct);
        else if (requireWrite && ctx.UnsafeBoxFailureDayId is { } failedDayId)
        {
            if (persistUnsafeBoxFailure is not null) await persistUnsafeBoxFailure(failedDayId);
            else await PersistUnsafeBoxFailureAsync(failedDayId, ct);
            return new PersistedUnsafeBoxFailure<T>(result.Error);
        }
        else if (requireWrite && _db is DbContext failedContext) failedContext.ChangeTracker.Clear();
        return result;
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
        if (day.Status == SubscriptionDayStatus.Open && day.LockAtUtc.HasValue)
            return Error.Conflict(MessageCodes.Calendar.SelectionLocked);
        if (day.ConfirmedAtUtc.HasValue && day.Status != SubscriptionDayStatus.SentToRestaurant)
            return Error.Conflict(MessageCodes.Calendar.SelectionLocked);
        if (LockedStatuses.Contains(day.Status) && day.Status != SubscriptionDayStatus.SentToRestaurant)
            return Error.Conflict(MessageCodes.Calendar.SelectionLocked);
        if (!allowAfterDeadline && day.GetEffectiveSelectionDeadlineUtc() is { } deadline && _clock.UtcNow >= deadline)
            return Error.Conflict(MessageCodes.Calendar.SelectionLocked);
        // A redispatched day may retain the previous attempt's confirmation timestamp.
        if (day.Status == SubscriptionDayStatus.SentToRestaurant) day.ConfirmedAtUtc = null;
        return Result.Success();
    }

    private bool CanEditDay(SubscriptionDay day)
        => ResolveEditBlockReason(day) is null;

    /// <summary>
    /// Single source for whether a day rejects edits and the reason returned to the customer.
    /// The SentToRestaurant redispatch exception stays: it keeps CanEdit true, so no reason here.
    /// </summary>
    private string? ResolveEditBlockReason(SubscriptionDay day)
    {
        if (day.Status == SubscriptionDayStatus.Cancelled) return CalendarEditBlockReasons.DayCancelled;
        if (day.Status == SubscriptionDayStatus.Frozen) return CalendarEditBlockReasons.DayFrozen;
        if (day.Status == SubscriptionDayStatus.Open && day.LockAtUtc.HasValue)
            return CalendarEditBlockReasons.DayLocked;
        if (day.ConfirmedAtUtc.HasValue && day.Status != SubscriptionDayStatus.SentToRestaurant)
            return CalendarEditBlockReasons.DayLocked;
        if (LockedStatuses.Contains(day.Status) && day.Status != SubscriptionDayStatus.SentToRestaurant)
            return CalendarEditBlockReasons.DayLocked;
        if (day.GetEffectiveSelectionDeadlineUtc() is { } deadline && _clock.UtcNow >= deadline)
            return CalendarEditBlockReasons.SelectionDeadlinePassed;
        return null;
    }

    private static IReadOnlyList<CustomerDayRestaurantDto> ListRestaurants(CalendarContext ctx, SubscriptionDay day, string? query = null)
    {
        var usage = ctx.Days
            .Where(x => x.Id != day.Id && x.RestaurantId is not null && CountedRestaurantStatuses.Contains(x.Status))
            .GroupBy(x => x.RestaurantId!.Value)
            .ToDictionary(x => x.Key, x => x.Count());

        var candidates = ctx.Restaurants.AsEnumerable();

        var targetRegionId = day.DeliveryRegionId ?? ctx.Subscription.RegionId;
        if (targetRegionId.HasValue && targetRegionId.Value != Guid.Empty)
        {
            candidates = candidates.Where(r =>
                ctx.ServiceRegions.Any(sr => sr.RestaurantId == r.RestaurantId && sr.RegionId == targetRegionId.Value && sr.IsActive));
        }

        var rows = new List<CustomerDayRestaurantDto>();
        foreach (var restaurant in candidates.OrderByDescending(x => x.IsInitiallySelected).ThenBy(x => x.NameEn).ThenBy(x => x.RestaurantId))
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
            var (dwStart, dwEnd) = ctx.DeliveryWindow(restaurant.RestaurantId, day.DeliveryRegionId);
            var tierInfo = ctx.TierInfo(restaurant.RestaurantId);
            rows.Add(new CustomerDayRestaurantDto(
                restaurant.RestaurantId, restaurant.NameAr, restaurant.NameEn, ctx.LogoUrl(restaurant.RestaurantId), restaurant.Tier,
                used, ctx.Limit, Math.Max(0, ctx.Limit - used), busy, used >= ctx.Limit, false, disabled, safeMeals.Count,
                ctx.RestaurantRating(restaurant.RestaurantId),
                ctx.RestaurantRatingCount(restaurant.RestaurantId),
                dwStart,
                dwEnd,
                FormatDeliveryTimeText(dwStart, dwEnd),
                tierInfo?.TierNameAr,
                tierInfo?.TierNameEn,
                tierInfo?.TierName,
                tierInfo?.TierLogoUrl));
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

    private static Result CurrentRestaurantEligibility(CalendarContext ctx, SubscriptionDay day)
    {
        var restaurant = ListRestaurants(ctx, day).FirstOrDefault(x => x.RestaurantId == day.RestaurantId);
        if (restaurant is null)
            return Error.Conflict((day.DeliveryRegionId ?? ctx.Subscription.RegionId) is { } regionId && regionId != Guid.Empty
                ? MessageCodes.Calendar.RestaurantCoverageUnavailable : MessageCodes.Calendar.RestaurantNotInPool);
        return restaurant.IsEnabled ? Result.Success()
            : Error.Conflict(restaurant.DisabledReason ?? MessageCodes.Calendar.RestaurantNotInPool);
    }

    private static string UnavailableBoxReason(CalendarContext ctx, SubscriptionDay day)
    {
        var restaurants = ListRestaurants(ctx, day);
        if (restaurants.Count == 0 && (day.DeliveryRegionId ?? ctx.Subscription.RegionId) is { } regionId && regionId != Guid.Empty)
            return MessageCodes.Calendar.RestaurantCoverageUnavailable;
        if (restaurants.Count > 0 && restaurants.All(x => x.DisabledReason == MessageCodes.Calendar.BoxNotReady))
            return MessageCodes.Calendar.BoxNotReady;
        return MessageCodes.Calendar.NoSafeBox;
    }

    private static bool MatchesSearch(CustomerDayRestaurantDto restaurant, string query)
        => (restaurant.NameAr is not null && restaurant.NameAr.Contains(query, StringComparison.OrdinalIgnoreCase))
           || (restaurant.NameEn is not null && restaurant.NameEn.Contains(query, StringComparison.OrdinalIgnoreCase));

    private PickedBox? TryPickFullBox(CalendarContext ctx, SubscriptionDay day, IReadOnlySet<Guid>? excludedRestaurants = null)
    {
        var listed = ListRestaurants(ctx, day)
            .Where(x => x.IsEnabled)
            .Where(x => excludedRestaurants?.Contains(x.RestaurantId) != true)
            .ToList();
        var ordered = listed
            .Where(x => x.DisabledReason is null || x.DisabledReason == MessageCodes.Calendar.RestaurantBusy || x.DisabledReason == MessageCodes.Calendar.QuotaExhausted)
            .OrderBy(x => day.RestaurantId == x.RestaurantId ? 0 : 1)
            .ThenBy(x => x.IsBusy)
            .ThenBy(x => x.IsQuotaExhausted)
            .ThenByDescending(x => DietaryScore(ctx, day, x.RestaurantId))
            .ThenByDescending(x => DislikeFreeScore(ctx, day, x.RestaurantId))
            .ToList();

        var existingIds = ctx.Selections.Where(x => x.SubscriptionDayId == day.Id).Select(x => x.MealId).ToHashSet();

        // Smart recommendation engine evaluation
        var candidates = new List<CandidateRestaurantMenu>();
        foreach (var r in listed)
        {
            var snapshot = ctx.RestaurantDays.FirstOrDefault(x => x.RestaurantId == r.RestaurantId && x.ServiceDate == day.ServiceDate);
            if (snapshot is null) continue;
            var meals = ParseMeals(snapshot.MealsJson);

            candidates.Add(new CandidateRestaurantMenu(
                r.RestaurantId,
                r.NameAr ?? string.Empty,
                r.NameEn ?? string.Empty,
                r.Rating ?? 4.0m,
                r.RatingCount,
                r.IsBusy,
                r.IsQuotaExhausted,
                r.UsedCount,
                r.Limit,
                meals));
        }

        var history = new List<ConsumptionHistoryItem>();
        foreach (var pastDay in ctx.Days.Where(d => d.ServiceDate < day.ServiceDate && d.RestaurantId != null))
        {
            var daySelections = ctx.Selections.Where(s => s.SubscriptionDayId == pastDay.Id).ToList();
            var daySnapshot = ctx.DaySnapshots.FirstOrDefault(s => s.SubscriptionDayId == pastDay.Id);
            var rdSnapshot = ctx.RestaurantDays.FirstOrDefault(rd => rd.RestaurantId == pastDay.RestaurantId && rd.ServiceDate == pastDay.ServiceDate);
            var mealsJson = daySnapshot?.MealsJson ?? rdSnapshot?.MealsJson;
            var meals = mealsJson != null ? ParseMeals(mealsJson) : [];
            var mealDict = meals.GroupBy(m => m.MealId).ToDictionary(g => g.Key, g => g.First());

            foreach (var sel in daySelections)
            {
                var slot = mealDict.TryGetValue(sel.MealId, out var mealItem) ? mealItem.Slot : "meal";
                var proteinType = mealItem is not null ? ProteinTypeClassifier.Classify(mealItem) : null;
                history.Add(new ConsumptionHistoryItem(
                    pastDay.ServiceDate,
                    pastDay.RestaurantId!.Value,
                    sel.MealId,
                    slot,
                    WasManuallySelected: !pastDay.IsAutoSelected,
                    ProteinType: proteinType));
            }
        }

        var recommendContext = new MealRecommendationContext(
            ctx.Subscription.CustomerUserId,
            ctx.Subscription.Id,
            day.ServiceDate,
            day.RestaurantId,
            ctx.Preferences,
            ctx.NutritionTarget,
            ctx.SlotRequirements,
            history,
            candidates,
            ctx.RejectedMealIds,
            LockedMealIds: existingIds.Count > 0 ? existingIds : null);

        var recResult = _recommendationEngine.RecommendBoxForDayAsync(recommendContext).GetAwaiter().GetResult();
        if (recResult is not null)
        {
            var chosenSnapshot = ctx.RestaurantDays.FirstOrDefault(x => x.RestaurantId == recResult.RestaurantId && x.ServiceDate == day.ServiceDate);
            if (chosenSnapshot is not null)
            {
                return new PickedBox(chosenSnapshot, recResult.SelectedMeals);
            }
        }

        // Fallback: If day already had a designated restaurant, try to complete with safe meals
        if (day.RestaurantId is { } currentId)
        {
            var current = ordered.FirstOrDefault(x => x.RestaurantId == currentId && !x.IsQuotaExhausted && x.DisabledReason is null);
            if (current is not null)
            {
                var currentBox = BuildFullBox(ctx, day, currentId, keepExisting: true);
                if (currentBox is not null) return currentBox;
            }
        }

        // Fallback: Ordered search across all eligible restaurants
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
        // One batched delivery projection for the whole calendar: the widget and these
        // day cards must report identical statuses from the same saved events.
        var deliveryByDay = await _deliveryRead.GetForDaysAsync(
            ctx.Days.Select(x => x.Id).ToList(), ct);
        var result = new List<CustomerCalendarDaySummaryDto>(ctx.Days.Count);
        foreach (var day in ctx.Days)
            result.Add(MapDaySummaryAsync(ctx, day, deliveryByDay.TryGetValue(day.Id, out var p) ? p : null));
        return result;
    }

    private CustomerCalendarDaySummaryDto MapDaySummaryAsync(
        CalendarContext ctx, SubscriptionDay day, Features.DriverDelivery.CustomerTracking.CustomerDeliveryDayProjection? delivery)
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
        var remaining = day.GetEffectiveSelectionDeadlineUtc() is { } deadline
            ? (int)Math.Max(0, Math.Ceiling((deadline - _clock.UtcNow).TotalSeconds))
            : (int?)null;
        var canEdit = ctx.CanEditDailyMeals && CanEditDay(day);
        var (dwStart, dwEnd) = restaurantId.HasValue
            ? ctx.DeliveryWindow(restaurantId.Value, day.DeliveryRegionId)
            : (null, null);
        var tierInfo = restaurantId.HasValue ? ctx.TierInfo(restaurantId.Value) : null;
        var programInfo = ctx.ProgramInfo();
        var (deliveryStatus, deliveryTextAr, deliveryTextEn, deliveryChangedAt, deliveryTrackable) = DeliveryFieldsOf(delivery);
        return new CustomerCalendarDaySummaryDto(
            day.Id, day.ServiceDate, restaurantId, frozen?.NameAr, frozen?.NameEn,
            restaurantId is { } logoRestaurantId ? ctx.LogoUrl(logoRestaurantId) : null,
            day.Status, MapUiStatus(day, filled, restaurantId, canEdit), day.IsAutoSelected, canEdit,
            day.GetEffectiveSelectionDeadlineUtc(), day.LockAtUtc, remaining, filled, ctx.RequiredCount,
            CustomerCalendarSlotLabels.ToDtos(ctx.SlotRequirements),
            day.Status == SubscriptionDayStatus.ReplacementRequired ? day.FailureReasonCode ?? MessageCodes.Calendar.NoSafeBox
                : day.Status == SubscriptionDayStatus.Frozen ? "subscription.day_frozen" : null,
            selected,
            restaurantId.HasValue ? ctx.RestaurantRating(restaurantId.Value) : null,
            restaurantId.HasValue ? ctx.RestaurantRatingCount(restaurantId.Value) : 0,
            dwStart,
            dwEnd,
            FormatDeliveryTimeText(dwStart, dwEnd),
            tierInfo?.Tier,
            tierInfo?.TierNameAr,
            tierInfo?.TierNameEn,
            tierInfo?.TierName,
            tierInfo?.TierLogoUrl,
            programInfo?.ProgramNameAr,
            programInfo?.ProgramNameEn,
            programInfo?.ProgramName,
            ctx.CanEditDailyMeals ? ResolveEditBlockReason(day) : ctx.EditPermissionReason,
            deliveryStatus,
            deliveryTextAr,
            deliveryTextEn,
            deliveryTextAr is null ? null : CurrentDeliveryText(deliveryTextAr, deliveryTextEn),
            deliveryChangedAt,
            deliveryTrackable);
    }

    private async Task<CustomerCalendarDayDto> MapDayAsync(CalendarContext ctx, SubscriptionDay day, CancellationToken ct)
    {
        var delivery = (await _deliveryRead.GetForDaysAsync([day.Id], ct)).TryGetValue(day.Id, out var projection)
            ? projection
            : null;
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
                    : ToMealDto(meal, ctx.Preferences, isSelected: true, media, ctx.NutritionTarget);
            })
            .ToList();
        var filled = selected.Count;
        var remaining = day.GetEffectiveSelectionDeadlineUtc() is { } deadline
            ? (int)Math.Max(0, Math.Ceiling((deadline - _clock.UtcNow).TotalSeconds))
            : (int?)null;
        var canEdit = ctx.CanEditDailyMeals && CanEditDay(day);
        var (dwStart, dwEnd) = restaurantId.HasValue
            ? ctx.DeliveryWindow(restaurantId.Value, day.DeliveryRegionId)
            : (null, null);
        var tierInfo = restaurantId.HasValue ? ctx.TierInfo(restaurantId.Value) : null;
        var programInfo = ctx.ProgramInfo();
        var nutritionTotals = SumSelectedNutrition(selected, ctx.NutritionTarget);
        var (deliveryStatus, deliveryTextAr, deliveryTextEn, deliveryChangedAt, deliveryTrackable) = DeliveryFieldsOf(delivery);
        return new CustomerCalendarDayDto(
            day.Id, day.ServiceDate, restaurantId, frozen?.NameAr, frozen?.NameEn,
            restaurantId is { } logoRestaurantId ? ctx.LogoUrl(logoRestaurantId) : null,
            day.Status, MapUiStatus(day, filled, restaurantId, canEdit), day.IsAutoSelected, canEdit,
            day.GetEffectiveSelectionDeadlineUtc(), day.LockAtUtc, remaining, filled, ctx.RequiredCount,
            CustomerCalendarSlotLabels.ToDtos(ctx.SlotRequirements),
            day.Status == SubscriptionDayStatus.ReplacementRequired ? day.FailureReasonCode ?? MessageCodes.Calendar.NoSafeBox
                : day.Status == SubscriptionDayStatus.Frozen ? "subscription.day_frozen" : null,
            nutritionTotals,
            selected,
            restaurantId.HasValue ? ctx.RestaurantRating(restaurantId.Value) : null,
            restaurantId.HasValue ? ctx.RestaurantRatingCount(restaurantId.Value) : 0,
            dwStart,
            dwEnd,
            FormatDeliveryTimeText(dwStart, dwEnd),
            tierInfo?.Tier,
            tierInfo?.TierNameAr,
            tierInfo?.TierNameEn,
            tierInfo?.TierName,
            tierInfo?.TierLogoUrl,
            programInfo?.ProgramNameAr,
            programInfo?.ProgramNameEn,
            programInfo?.ProgramName,
            nutritionTotals.ProteinPercentage,
            nutritionTotals.CarbsPercentage,
            nutritionTotals.FatPercentage,
            nutritionTotals.CaloriesPercentage,
            ctx.CanEditDailyMeals ? ResolveEditBlockReason(day) : ctx.EditPermissionReason,
            deliveryStatus,
            deliveryTextAr,
            deliveryTextEn,
            deliveryTextAr is null ? null : CurrentDeliveryText(deliveryTextAr, deliveryTextEn),
            deliveryChangedAt,
            deliveryTrackable);
    }

    /// <summary>Additive delivery fields sourced from the same shared projection the
    /// widget uses; null when the projection is absent (legacy engine constructions).</summary>
    private static (string? Status, string? TextAr, string? TextEn, DateTime? ChangedAtUtc, bool IsTrackable) DeliveryFieldsOf(
        Features.DriverDelivery.CustomerTracking.CustomerDeliveryDayProjection? delivery)
    {
        if (delivery is null)
        {
            return (null, null, null, null, false);
        }

        var snapshot = delivery.Snapshot;
        DateTime? changedAt = null;
        foreach (var instant in new[] { snapshot.ReadyAtUtc, snapshot.PickedUpAtUtc, snapshot.ArrivedAtUtc, snapshot.DeliveredAtUtc })
        {
            if (instant.HasValue && (changedAt is null || instant.Value > changedAt.Value))
            {
                changedAt = instant;
            }
        }

        return (snapshot.DeliveryStatus, snapshot.StatusTextAr, snapshot.StatusTextEn, changedAt, snapshot.IsTrackable);
    }

    private string CurrentDeliveryText(string textAr, string? textEn)
        => _messages?.IsArabic() != false ? textAr : (textEn ?? textAr);

    private static CustomerDayNutritionTotalsDto SumSelectedNutrition(IReadOnlyList<CustomerCalendarMealDto> selected, ProgramDailyNutritionTargetDto? nutritionTarget)
    {
        var calories = selected.Sum(x => x.Calories ?? 0m);
        var proteinG = selected.Sum(x => x.ProteinG ?? 0m);
        var carbsG = selected.Sum(x => x.CarbsG ?? 0m);
        var fatG = selected.Sum(x => x.FatG ?? 0m);

        var totalMacroCalories = (proteinG * 4m) + (carbsG * 4m) + (fatG * 9m);
        var proteinPct = totalMacroCalories > 0 ? Math.Round((proteinG * 4m / totalMacroCalories) * 100m, 1) : 0m;
        var carbsPct = totalMacroCalories > 0 ? Math.Round((carbsG * 4m / totalMacroCalories) * 100m, 1) : 0m;
        var fatPct = totalMacroCalories > 0 ? Math.Round((fatG * 9m / totalMacroCalories) * 100m, 1) : 0m;
        var caloriesPct = nutritionTarget is { SuggestedKcal: > 0 }
            ? Math.Round((calories / nutritionTarget.SuggestedKcal) * 100m, 1) : 0m;

        return new CustomerDayNutritionTotalsDto(
            calories,
            proteinG,
            carbsG,
            fatG,
            proteinPct,
            carbsPct,
            fatPct,
            caloriesPct);
    }

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

    private static string MapUiStatus(SubscriptionDay day, int filled, Guid? restaurantId, bool canEdit)
    {
        if (day.Status == SubscriptionDayStatus.Cancelled) return CustomerCalendarUiStatus.Cancelled;
        if (day.Status == SubscriptionDayStatus.Frozen) return CustomerCalendarUiStatus.Frozen;
        if (day.Status is SubscriptionDayStatus.ReplacementRequired or SubscriptionDayStatus.ReplacementWindowOpen)
            return CustomerCalendarUiStatus.ReplacementRequired;
        if (!canEdit) return CustomerCalendarUiStatus.Locked;
        if (day.Status == SubscriptionDayStatus.SentToRestaurant && canEdit)
            return CustomerCalendarUiStatus.AwaitingRestaurant;
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
            .Select(x => new
            {
                x.Id,
                x.NameAr,
                x.NameEn,
                x.RestaurantIconUrl,
                x.MealIconUrl,
                x.IsLocal,
                x.IsOriginVisibleToCustomers,
                x.OriginCountryCode,
            })
            .ToListAsync(ct);
        var countryCodes = rows
            .Where(x => x.IsOriginVisibleToCustomers && !string.IsNullOrWhiteSpace(x.OriginCountryCode))
            .Select(x => x.OriginCountryCode!.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();
        var countryByCode = countryCodes.Count == 0
            ? new Dictionary<string, (string NameAr, string NameEn, string? FlagEmoji)>()
            : (await _db.Countries.AsNoTracking()
                .Where(x => countryCodes.Contains(x.Code))
                .Select(x => new { x.Code, x.NameAr, x.NameEn, x.FlagEmoji })
                .ToListAsync(ct))
            .GroupBy(x => x.Code.Trim().ToUpperInvariant())
            .ToDictionary(
                g => g.Key,
                g => (g.First().NameAr, g.First().NameEn, g.First().FlagEmoji));

        return rows
            .Select(x =>
            {
                var showOrigin = x.IsOriginVisibleToCustomers && !string.IsNullOrWhiteSpace(x.OriginCountryCode);
                var code = showOrigin ? x.OriginCountryCode!.Trim().ToUpperInvariant() : null;
                countryByCode.TryGetValue(code ?? string.Empty, out var country);
                return new CustomerRestaurantCoreIngredientDto(
                    x.Id,
                    x.NameAr,
                    x.NameEn,
                    CatalogImageStorage.ToContentUrl(x.RestaurantIconUrl ?? x.MealIconUrl),
                    showOrigin && x.IsLocal,
                    code,
                    showOrigin ? country.NameAr : null,
                    showOrigin ? country.NameEn : null,
                    showOrigin ? country.FlagEmoji : null);
            })
            .ToList();
    }

    private static readonly Dictionary<string, (string Ar, string En)> KnownAllergens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GLUTEN"] = ("جلوتين", "Gluten"),
        ["EGGS"] = ("بيض", "Eggs"),
        ["MILK"] = ("لاكتوز", "Lactose"),
        ["LACTOSE"] = ("لاكتوز", "Lactose"),
        ["PEANUTS"] = ("فول سوداني", "Peanuts"),
        ["TREE_NUTS"] = ("مكسرات", "Nuts"),
        ["NUTS"] = ("مكسرات", "Nuts"),
        ["FISH"] = ("أسماك", "Fish"),
        ["CRUSTACEANS"] = ("قشريات", "Crustaceans"),
        ["SOY"] = ("صويا", "Soy"),
        ["SOYBEANS"] = ("صويا", "Soy"),
        ["SESAME"] = ("سمسم", "Sesame"),
        ["CELERY"] = ("كرفس", "Celery"),
        ["MUSTARD"] = ("خردل", "Mustard"),
        ["SULPHITES"] = ("كبريتيت", "Sulphites"),
        ["LUPIN"] = ("ترمس", "Lupin"),
        ["MOLLUSCS"] = ("رخويات", "Molluscs"),
    };

    private static (IReadOnlyList<string> Allergens, IReadOnlyList<string> AllergensAr, IReadOnlyList<string> AllergensEn, IReadOnlyList<CustomerCalendarMealAllergenDto> Details) ResolveMealAllergens(
        MealSnapshotItem meal)
    {
        var allCodes = meal.PresentAllergenCodes
            .Concat(meal.Ingredients.SelectMany(i => i.AllergenCodes))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var namesAr = new List<string>(allCodes.Count);
        var namesEn = new List<string>(allCodes.Count);
        var details = new List<CustomerCalendarMealAllergenDto>(allCodes.Count);

        foreach (var code in allCodes)
        {
            if (KnownAllergens.TryGetValue(code, out var known))
            {
                namesAr.Add(known.Ar);
                namesEn.Add(known.En);
                details.Add(new CustomerCalendarMealAllergenDto(code, known.Ar, known.En));
            }
            else
            {
                namesAr.Add(code);
                namesEn.Add(code);
                details.Add(new CustomerCalendarMealAllergenDto(code, code, code));
            }
        }

        return (namesAr, namesAr, namesEn, details);
    }

    private static CustomerCalendarMealDto ToMealDto(
        MealSnapshotItem meal,
        MealPreferenceFilter.CustomerPreferenceState preferences,
        bool isSelected = false,
        IReadOnlyDictionary<Guid, IngredientMedia>? ingredientMedia = null,
        ProgramDailyNutritionTargetDto? nutritionTarget = null)
    {
        var proteinG = meal.ProteinG ?? 0m;
        var carbsG = meal.CarbsG ?? 0m;
        var fatG = meal.FatG ?? 0m;
        var calories = meal.Calories ?? 0m;
        var totalMacroCalories = (proteinG * 4m) + (carbsG * 4m) + (fatG * 9m);
        var proteinPct = totalMacroCalories > 0 ? Math.Round((proteinG * 4m / totalMacroCalories) * 100m, 1) : (decimal?)null;
        var carbsPct = totalMacroCalories > 0 ? Math.Round((carbsG * 4m / totalMacroCalories) * 100m, 1) : (decimal?)null;
        var fatPct = totalMacroCalories > 0 ? Math.Round((fatG * 9m / totalMacroCalories) * 100m, 1) : (decimal?)null;
        var caloriesPct = meal.Calories.HasValue && nutritionTarget is { SuggestedKcal: > 0 }
            ? Math.Round((calories / nutritionTarget.SuggestedKcal) * 100m, 1) : (decimal?)null;
        var (allergens, allergensAr, allergensEn, allergenDetails) = ResolveMealAllergens(meal);

        return new(
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
            isSelected,
            proteinPct,
            carbsPct,
            fatPct,
            caloriesPct,
            allergens,
            allergensAr,
            allergensEn,
            allergenDetails);
    }

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

    private static string? FormatDeliveryTimeText(TimeOnly? start, TimeOnly? end)
    {
        if (start is null || end is null) return null;
        var isArabic = !CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase);
        if (isArabic)
        {
            static string FormatAr(TimeOnly t)
            {
                var hour = t.Hour % 12;
                if (hour == 0) hour = 12;
                var period = t.Hour < 12 ? "ص" : "م";
                var min = t.Minute == 0 ? "00" : t.Minute.ToString("D2");
                return $"{hour}:{min} {period}";
            }
            return $"{FormatAr(start.Value)} - {FormatAr(end.Value)}";
        }
        else
        {
            static string FormatEn(TimeOnly t)
            {
                var hour = t.Hour % 12;
                if (hour == 0) hour = 12;
                var period = t.Hour < 12 ? "AM" : "PM";
                var min = t.Minute == 0 ? "00" : t.Minute.ToString("D2");
                return $"{hour}:{min} {period}";
            }
            return $"{FormatEn(start.Value)} - {FormatEn(end.Value)}";
        }
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
            IReadOnlyDictionary<Guid, string?> restaurantLogos,
            IReadOnlyDictionary<Guid, (decimal? Rating, int Count)> restaurantRatings,
            IReadOnlyList<MealMate.Domain.RestaurantOperations.RestaurantServiceRegion> serviceRegions,
            Program? program,
            TierLabelsSettings tierLabels,
            ProgramDailyNutritionTargetDto? nutritionTarget = null,
            IReadOnlySet<Guid>? rejectedMealIds = null)
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
            RestaurantRatings = restaurantRatings;
            ServiceRegions = serviceRegions;
            Program = program;
            TierLabels = tierLabels;
            NutritionTarget = nutritionTarget;
            RejectedMealIds = rejectedMealIds ?? new HashSet<Guid>();
        }

        public Subscription Subscription { get; }
        public Guid? UnsafeBoxFailureDayId { get; set; }
        public bool CanEditDailyMeals { get; set; } = true;
        public bool CanViewPlan { get; set; } = true;
        public string? EditPermissionReason { get; set; }
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
        public IReadOnlyDictionary<Guid, (decimal? Rating, int Count)> RestaurantRatings { get; }
        public IReadOnlyList<MealMate.Domain.RestaurantOperations.RestaurantServiceRegion> ServiceRegions { get; }
        public Program? Program { get; }
        public TierLabelsSettings TierLabels { get; }
        public ProgramDailyNutritionTargetDto? NutritionTarget { get; }
        public IReadOnlySet<Guid> RejectedMealIds { get; }
        public int RequiredCount => SlotRequirements.Sum(x => x.Count);
        public SubscriptionDay? Day(DateOnly date) => Days.FirstOrDefault(x => x.ServiceDate == date);
        public string? LogoUrl(Guid restaurantId) => RestaurantLogos.GetValueOrDefault(restaurantId);

        public decimal? RestaurantRating(Guid restaurantId)
            => RestaurantRatings.TryGetValue(restaurantId, out var r) ? r.Rating : null;

        public int RestaurantRatingCount(Guid restaurantId)
            => RestaurantRatings.TryGetValue(restaurantId, out var r) ? r.Count : 0;

        public (TimeOnly? Start, TimeOnly? End) DeliveryWindow(Guid restaurantId, Guid? dayRegionId)
        {
            var region = dayRegionId ?? Subscription.RegionId;
            if (region.HasValue && region.Value != Guid.Empty)
            {
                var match = ServiceRegions.FirstOrDefault(x => x.RestaurantId == restaurantId && x.RegionId == region.Value && x.IsActive);
                if (match is not null)
                    return (match.DeliveryWindowStart, match.DeliveryWindowEnd);
                return (null, null);
            }
            if (Subscription.FamilyGroupId.HasValue) return (null, null);
            var any = ServiceRegions.FirstOrDefault(x => x.RestaurantId == restaurantId && x.IsActive);
            if (any is not null)
                return (any.DeliveryWindowStart, any.DeliveryWindowEnd);
            return (null, null);
        }

        public (RestaurantTier Tier, string TierNameAr, string TierNameEn, string TierName, string? TierLogoUrl)? TierInfo(Guid restaurantId)
        {
            var restaurant = Restaurants.FirstOrDefault(x => x.RestaurantId == restaurantId);
            if (restaurant is null) return null;
            var (nameAr, nameEn, logoUrl) = ResolveTierInfo(restaurant.Tier, TierLabels);
            var isArabic = !CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase);
            var localized = isArabic ? nameAr : (string.IsNullOrWhiteSpace(nameEn) ? nameAr : nameEn);
            return (restaurant.Tier, nameAr, nameEn, localized, logoUrl);
        }

        public (string ProgramNameAr, string ProgramNameEn, string ProgramName)? ProgramInfo()
        {
            if (!CanViewPlan || Program is null) return null;
            var isArabic = !CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase);
            var localized = isArabic ? Program.NameAr : (string.IsNullOrWhiteSpace(Program.NameEn) ? Program.NameAr : Program.NameEn);
            return (Program.NameAr, Program.NameEn, localized);
        }

        private static (string NameAr, string NameEn, string? LogoUrl) ResolveTierInfo(RestaurantTier tier, TierLabelsSettings labels) => tier switch
        {
            RestaurantTier.Basic => (labels.BasicAr, labels.BasicEn, CatalogImageStorage.ToContentUrl(labels.BasicLogoStorageKey)),
            RestaurantTier.Platinum => (labels.PlatinumAr, labels.PlatinumEn, CatalogImageStorage.ToContentUrl(labels.PlatinumLogoStorageKey)),
            RestaurantTier.Elite => (labels.EliteAr, labels.EliteEn, CatalogImageStorage.ToContentUrl(labels.EliteLogoStorageKey)),
            _ => (tier.ToString(), tier.ToString(), null),
        };
    }
}
