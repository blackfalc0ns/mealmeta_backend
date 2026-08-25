using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MealMate.Application.Features.CustomerCheckout;

public sealed record SlotRequirementDto(string Slot, int Count);

public static class SlotRequirementJson
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<SlotRequirementDto> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
            if (document.RootElement.GetArrayLength() == 0) return [];
            if (document.RootElement[0].ValueKind == JsonValueKind.String)
                return document.RootElement.EnumerateArray()
                    .Select(x => x.GetString()?.Trim().ToLowerInvariant())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .GroupBy(x => x!, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new SlotRequirementDto(x.Key, x.Count()))
                    .ToList();
            return (JsonSerializer.Deserialize<List<SlotRequirementDto>>(json, Options) ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x.Slot) && x.Count > 0)
                .GroupBy(x => x.Slot.Trim().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase)
                .Select(x => new SlotRequirementDto(x.Key, x.Sum(y => y.Count)))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
public sealed record ResolvedMenuAssignment(Guid MealId, string Slot, int SortOrder, bool IsPrimary, Guid? MenuDayId);
public sealed record ResolvedMenuForDate(Guid MenuPlanId, Guid MenuPlanVersionId, ScheduleMode Mode, DateOnly ServiceDate, IReadOnlyList<ResolvedMenuAssignment> Assignments);

public interface ISubscriptionScheduleService
{
    IReadOnlyList<DateOnly> GetServiceDates(DateOnly startDate, int durationDays, bool includeFriday);
    DateOnly GetEndDate(DateOnly startDate, int durationDays, bool includeFriday);
    int GetFridayAddonDays(int durationDays);
    int CountFridays(IReadOnlyList<DateOnly> serviceDates);
    DateTime GetSelectionDeadlineUtc(DateOnly serviceDate);
    bool IsStartAllowed(DateOnly startDate);
    /// <summary>Kuwait calendar date for "today" comparisons on start-day pickers.</summary>
    DateOnly GetKuwaitToday();
    /// <summary>
    /// User-chosen subscription start day → first delivery box date.
    /// Rule: first delivery = chosenStartDate + 3 calendar days (72h), then skip Friday when excluded.
    /// </summary>
    DateOnly ResolveFirstDeliveryDate(DateOnly chosenStartDate, bool includeFriday);
    /// <summary>
    /// Validates the user-chosen start day and returns the first delivery date used as Subscription.StartDate.
    /// Error codes: checkout.start_date_required, checkout.start_date_invalid, checkout.start_date_too_soon.
    /// </summary>
    bool TryResolveCheckoutStart(DateOnly? chosenStartDate, bool includeFriday, out DateOnly firstDeliveryDate, out string? errorCode);
}

public sealed class SubscriptionScheduleService : ISubscriptionScheduleService
{
    private readonly IDateTimeProvider _clock;
    private static readonly TimeZoneInfo KuwaitTimeZone = ResolveKuwaitTimeZone();

    public SubscriptionScheduleService(IDateTimeProvider clock) => _clock = clock;

    public IReadOnlyList<DateOnly> GetServiceDates(DateOnly startDate, int durationDays, bool includeFriday)
    {
        if (durationDays <= 0) return [];
        // DurationDays is the regular service quota. Friday is an optional fixed add-on
        // (6 => 1, 12 => 2, 26 => 4), so a 26-day plan becomes 30 service days.
        var fridayQuota = includeFriday ? GetFridayAddonDays(durationDays) : 0;
        var result = new List<DateOnly>(durationDays + fridayQuota);
        var regularServiceDays = 0;
        var fridayServiceDays = 0;
        for (var cursor = startDate;
             regularServiceDays < durationDays || fridayServiceDays < fridayQuota;
             cursor = cursor.AddDays(1))
        {
            if (cursor.DayOfWeek == DayOfWeek.Friday)
            {
                if (fridayServiceDays < fridayQuota)
                {
                    result.Add(cursor);
                    fridayServiceDays++;
                }
                continue;
            }

            if (regularServiceDays < durationDays)
            {
                result.Add(cursor);
                regularServiceDays++;
            }
        }
        return result.OrderBy(x => x).ToList();
    }

    public DateOnly GetEndDate(DateOnly startDate, int durationDays, bool includeFriday)
        => GetServiceDates(startDate, durationDays, includeFriday).LastOrDefault();

    public int GetFridayAddonDays(int durationDays)
        => durationDays <= 0 ? 0 : Math.Max(1, durationDays / 6);

    public int CountFridays(IReadOnlyList<DateOnly> serviceDates)
        => serviceDates.Count(x => x.DayOfWeek == DayOfWeek.Friday);

    public DateTime GetSelectionDeadlineUtc(DateOnly serviceDate)
    {
        var localStart = DateTime.SpecifyKind(serviceDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var utcStart = TimeZoneInfo.ConvertTimeToUtc(localStart, KuwaitTimeZone);
        return utcStart.AddHours(-72);
    }

    public bool IsStartAllowed(DateOnly startDate)
        => _clock.UtcNow < GetSelectionDeadlineUtc(startDate);

    public DateOnly GetKuwaitToday()
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, KuwaitTimeZone);
        return DateOnly.FromDateTime(local);
    }

    public DateOnly ResolveFirstDeliveryDate(DateOnly chosenStartDate, bool includeFriday)
    {
        var firstDelivery = chosenStartDate.AddDays(3);
        if (includeFriday) return firstDelivery;
        while (firstDelivery.DayOfWeek == DayOfWeek.Friday)
            firstDelivery = firstDelivery.AddDays(1);
        return firstDelivery;
    }

    public bool TryResolveCheckoutStart(
        DateOnly? chosenStartDate,
        bool includeFriday,
        out DateOnly firstDeliveryDate,
        out string? errorCode)
    {
        firstDeliveryDate = default;
        errorCode = null;
        if (chosenStartDate is null || chosenStartDate == default)
        {
            errorCode = "checkout.start_date_required";
            return false;
        }

        var today = GetKuwaitToday();
        if (chosenStartDate.Value < today)
        {
            errorCode = "checkout.start_date_invalid";
            return false;
        }

        firstDeliveryDate = ResolveFirstDeliveryDate(chosenStartDate.Value, includeFriday);
        return true;
    }

    private static TimeZoneInfo ResolveKuwaitTimeZone()
    {
        foreach (var id in new[] { "Asia/Kuwait", "Arab Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { } catch (InvalidTimeZoneException) { }
        return TimeZoneInfo.CreateCustomTimeZone("Kuwait", TimeSpan.FromHours(3), "Kuwait", "Kuwait");
    }
}

public interface IBundleSlotRequirementService
{
    Task<IReadOnlyList<SlotRequirementDto>> ResolveAsync(Guid programId, Guid bundleId, CancellationToken cancellationToken = default);
}

public sealed class BundleSlotRequirementService : IBundleSlotRequirementService
{
    private readonly IAppDbContext _db;
    public BundleSlotRequirementService(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<SlotRequirementDto>> ResolveAsync(Guid programId, Guid bundleId, CancellationToken ct = default)
    {
        var bundle = await _db.Bundles.AsNoTracking().Where(x => x.Id == bundleId && x.ProgramId == programId && x.IsActive && x.Status == ProgramStatus.Active)
            .Select(x => new { x.BreakfastCount, x.LunchCount, x.DinnerCount, x.SnackCount, x.MealsPerDay }).FirstOrDefaultAsync(ct);
        if (bundle is null) return [];
        var rows = new[]
        {
            new SlotRequirementDto("breakfast", bundle.BreakfastCount),
            new SlotRequirementDto("lunch", bundle.LunchCount),
            new SlotRequirementDto("dinner", bundle.DinnerCount),
            new SlotRequirementDto("snack", bundle.SnackCount)
        }.Where(x => x.Count > 0).ToList();
        return rows.Sum(x => x.Count) == bundle.MealsPerDay ? rows : [];
    }
}

public interface IMenuPlanResolutionService
{
    Task<ResolvedMenuForDate?> ResolveAsync(Guid restaurantId, Guid programId, DateOnly serviceDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<(Guid RestaurantId, DateOnly ServiceDate), ResolvedMenuForDate>> ResolveManyAsync(IReadOnlyCollection<Guid> restaurantIds, Guid programId, IReadOnlyCollection<DateOnly> serviceDates, CancellationToken cancellationToken = default);
}

public sealed class MenuPlanResolutionService : IMenuPlanResolutionService
{
    private readonly IAppDbContext _db;
    public MenuPlanResolutionService(IAppDbContext db) => _db = db;

    public async Task<ResolvedMenuForDate?> ResolveAsync(Guid restaurantId, Guid programId, DateOnly date, CancellationToken ct = default)
    {
        var rows = await ResolveManyAsync([restaurantId], programId, [date], ct);
        return rows.GetValueOrDefault((restaurantId, date));
    }

    public async Task<IReadOnlyDictionary<(Guid RestaurantId, DateOnly ServiceDate), ResolvedMenuForDate>> ResolveManyAsync(IReadOnlyCollection<Guid> restaurantIds, Guid programId, IReadOnlyCollection<DateOnly> serviceDates, CancellationToken ct = default)
    {
        var ids = restaurantIds.Where(x => x != Guid.Empty).Distinct().ToList();
        var dates = serviceDates.Distinct().OrderBy(x => x).ToList();
        if (ids.Count == 0 || dates.Count == 0) return new Dictionary<(Guid, DateOnly), ResolvedMenuForDate>();
        var minDate = dates[0];
        var maxDate = dates[^1];
        var candidates = await (from version in _db.MenuPlanVersions.AsNoTracking()
                                join plan in _db.MenuPlans.AsNoTracking() on version.MenuPlanId equals plan.Id
                                join link in _db.MenuPlanPrograms.AsNoTracking() on plan.Id equals link.MenuPlanId
                                where plan.RestaurantId != null && ids.Contains(plan.RestaurantId.Value) && !plan.IsTemplate && !plan.IsDeleted && !link.IsDeleted
                                    && link.ProgramId == programId && link.EffectiveFrom <= maxDate && (link.EffectiveTo == null || link.EffectiveTo >= minDate)
                                    && version.Status == VerificationStatus.Approved && !version.IsPaused && version.EffectiveFrom <= maxDate
                                select new MenuCandidate(plan.RestaurantId!.Value, plan.Id, plan.Mode, version.Id, version.EffectiveFrom, version.VersionNumber, link.EffectiveFrom, link.EffectiveTo))
            .ToListAsync(ct);
        if (candidates.Count == 0) return new Dictionary<(Guid, DateOnly), ResolvedMenuForDate>();
        var earliestEffective = candidates.Min(x => x.VersionEffectiveFrom);
        var schedules = await _db.RestaurantOperatingSchedules.AsNoTracking().Where(x => ids.Contains(x.RestaurantId)).ToListAsync(ct);
        var exceptions = await _db.RestaurantOperatingDayExceptions.AsNoTracking().Where(x => ids.Contains(x.RestaurantId) && x.Date >= earliestEffective && x.Date <= maxDate).ToListAsync(ct);
        var versionIds = candidates.Select(x => x.VersionId).Distinct().ToList();
        var menuDays = await _db.MenuDays.AsNoTracking().Where(x => versionIds.Contains(x.MenuPlanVersionId) && !x.IsDeleted)
            .OrderBy(x => x.DayNumber).Select(x => new MenuDayRow(x.Id, x.MenuPlanVersionId, x.DayNumber)).ToListAsync(ct);
        var dayIds = menuDays.Select(x => x.Id).ToList();
        var cycleAssignments = await _db.MenuDayMeals.AsNoTracking().Where(x => dayIds.Contains(x.MenuDayId) && x.ProgramId == programId && !x.IsDeleted)
            .Select(x => new CycleAssignmentRow(x.MenuDayId, x.MealId, x.Slot, x.SortOrder, x.IsPrimary)).ToListAsync(ct);
        var weekdayAssignments = await _db.WeekdayMealAssignments.AsNoTracking().Where(x => versionIds.Contains(x.MenuPlanVersionId) && x.ProgramId == programId && !x.IsDeleted)
            .Select(x => new WeekdayAssignmentRow(x.MenuPlanVersionId, x.DayOfWeek, x.MealId, x.Slot, x.SortOrder, x.IsPrimary)).ToListAsync(ct);

        bool Working(Guid restaurantId, DateOnly value)
        {
            var exception = exceptions.FirstOrDefault(x => x.RestaurantId == restaurantId && x.Date == value);
            if (exception is not null) return exception.IsWorkingDay;
            var restaurantSchedules = schedules.Where(x => x.RestaurantId == restaurantId).ToList();
            return restaurantSchedules.Count == 0
                ? value.DayOfWeek != DayOfWeek.Friday
                : restaurantSchedules.Any(x => x.DayOfWeek == value.DayOfWeek && x.IsWorkingDay);
        }

        var result = new Dictionary<(Guid, DateOnly), ResolvedMenuForDate>();
        foreach (var restaurantId in ids)
        foreach (var date in dates)
        {
            if (!Working(restaurantId, date)) continue;
            var selected = candidates.Where(x => x.RestaurantId == restaurantId && x.VersionEffectiveFrom <= date
                    && x.ProgramEffectiveFrom <= date && (x.ProgramEffectiveTo == null || x.ProgramEffectiveTo >= date))
                .OrderByDescending(x => x.VersionEffectiveFrom).ThenByDescending(x => x.VersionNumber).FirstOrDefault();
            if (selected is null) continue;
            if (selected.Mode == ScheduleMode.Weekday)
            {
                var rows = weekdayAssignments.Where(x => x.VersionId == selected.VersionId && x.DayOfWeek == date.DayOfWeek)
                    .OrderBy(x => x.Slot).ThenBy(x => x.SortOrder)
                    .Select(x => new ResolvedMenuAssignment(x.MealId, x.Slot, x.SortOrder, x.IsPrimary, null)).ToList();
                result[(restaurantId, date)] = new(selected.PlanId, selected.VersionId, selected.Mode, date, rows);
                continue;
            }
            var days = menuDays.Where(x => x.VersionId == selected.VersionId).OrderBy(x => x.DayNumber).ToList();
            if (days.Count == 0) continue;
            var workingIndex = 0;
            for (var cursor = selected.VersionEffectiveFrom; cursor < date; cursor = cursor.AddDays(1))
                if (Working(restaurantId, cursor)) workingIndex++;
            var dayId = days[workingIndex % days.Count].Id;
            var assignments = cycleAssignments.Where(x => x.MenuDayId == dayId).OrderBy(x => x.Slot).ThenBy(x => x.SortOrder)
                .Select(x => new ResolvedMenuAssignment(x.MealId, x.Slot, x.SortOrder, x.IsPrimary, dayId)).ToList();
            result[(restaurantId, date)] = new(selected.PlanId, selected.VersionId, selected.Mode, date, assignments);
        }
        return result;
    }

    private sealed record MenuCandidate(Guid RestaurantId, Guid PlanId, ScheduleMode Mode, Guid VersionId, DateOnly VersionEffectiveFrom, int VersionNumber, DateOnly ProgramEffectiveFrom, DateOnly? ProgramEffectiveTo);
    private sealed record MenuDayRow(Guid Id, Guid VersionId, int DayNumber);
    private sealed record CycleAssignmentRow(Guid MenuDayId, Guid MealId, string Slot, int SortOrder, bool IsPrimary);
    private sealed record WeekdayAssignmentRow(Guid VersionId, DayOfWeek DayOfWeek, Guid MealId, string Slot, int SortOrder, bool IsPrimary);
}
