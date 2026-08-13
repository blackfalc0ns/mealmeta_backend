using System.Text.Json;
using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.CustomerCheckout.BackgroundJobs;

public sealed class CapacityHoldExpiryJob(IServiceScopeFactory scopes, ILogger<CapacityHoldExpiryJob> logger)
    : ScheduledJob(scopes, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(1);
    protected override string JobName => nameof(CapacityHoldExpiryJob);
    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var result = await services.GetRequiredService<ICapacityHoldExpiryService>().ExpireDueAsync(cancellationToken);
        if (result.HoldsExpired > 0)
            logger.LogInformation("Expired {HoldCount} checkout holds and {SubscriptionCount} subscriptions.", result.HoldsExpired, result.SubscriptionsExpired);
    }
}

public sealed class SubscriptionFulfillmentRetryJob(IServiceScopeFactory scopes, ILogger<SubscriptionFulfillmentRetryJob> logger)
    : ScheduledJob(scopes, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(1);
    protected override string JobName => nameof(SubscriptionFulfillmentRetryJob);
    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<IAppDbContext>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var candidates = await db.Subscriptions.AsNoTracking()
            .Where(x => x.Status == SubscriptionStatus.Paid && x.FulfillmentCompletedAtUtc == null)
            .OrderBy(x => x.LastFulfillmentAttemptUtc).Take(100)
            .Select(x => new { x.Id, x.FulfillmentAttemptCount, x.LastFulfillmentAttemptUtc })
            .ToListAsync(cancellationToken);
        var fulfillment = services.GetRequiredService<ISubscriptionFulfillmentService>();
        var attempted = 0;
        foreach (var candidate in candidates)
        {
            var delay = Math.Min(30, Math.Pow(2, Math.Max(0, candidate.FulfillmentAttemptCount - 1)));
            if (candidate.LastFulfillmentAttemptUtc?.AddMinutes(delay) > clock.UtcNow) continue;
            await fulfillment.TryFulfillAsync(candidate.Id, cancellationToken);
            attempted++;
        }
        if (attempted > 0) logger.LogInformation("Retried {Count} paid subscription fulfillments.", attempted);
    }
}

public sealed class SubscriptionRestaurantPoolBackfillJob(IServiceScopeFactory scopes, ILogger<SubscriptionRestaurantPoolBackfillJob> logger)
    : ScheduledJob(scopes, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(10);
    protected override string JobName => nameof(SubscriptionRestaurantPoolBackfillJob);
    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var result = await services.GetRequiredService<ISubscriptionRestaurantPoolBackfillService>().RunAsync(100, cancellationToken);
        if (result.Completed + result.Unresolved > 0)
            logger.LogInformation("Restaurant pool backfill completed {Completed}; unresolved {Unresolved}.", result.Completed, result.Unresolved);
    }
}

public sealed class SubscriptionDayLockJob(IServiceScopeFactory scopes, ILogger<SubscriptionDayLockJob> logger)
    : ScheduledJob(scopes, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);
    protected override string JobName => nameof(SubscriptionDayLockJob);

    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<IAppDbContext>();
        var now = services.GetRequiredService<IDateTimeProvider>().UtcNow;
        var days = await db.SubscriptionDays.Where(x =>
                (x.Status == SubscriptionDayStatus.Open || x.Status == SubscriptionDayStatus.AutoSelected || x.Status == SubscriptionDayStatus.CustomerSelected)
                && x.SelectionDeadlineUtc != null && x.SelectionDeadlineUtc <= now && x.ResolvedMenuPlanVersionId != null)
            .ToListAsync(cancellationToken);
        foreach (var day in days)
        {
            var snapshot = await db.SubscriptionDayMealSnapshots.AsNoTracking().FirstOrDefaultAsync(x => x.SubscriptionDayId == day.Id, cancellationToken);
            if (snapshot is not null && !await FillSafeSelectionsAsync(db, day, snapshot, now, cancellationToken))
                continue;
            day.Status = SubscriptionDayStatus.Locked72h;
            day.LockAtUtc = now;
        }
        if (days.Count > 0) await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Processed {Count} due subscription days.", days.Count);
    }

    private static async Task<bool> FillSafeSelectionsAsync(IAppDbContext db, SubscriptionDay day, SubscriptionDayMealSnapshot snapshot, DateTime now, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.AsNoTracking().Where(x => x.Id == day.SubscriptionId)
            .Select(x => new { x.ProgramId, x.CustomerUserId }).FirstAsync(ct);
        var meals = JsonSerializer.Deserialize<List<MealSnapshotItem>>(snapshot.MealsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        var requirements = SlotRequirementJson.Parse(snapshot.RequiredSlotsJson);
        var preferences = await db.CustomerAllergyPreferences.AsNoTracking().Where(x => x.CustomerUserId == subscription.CustomerUserId).ToListAsync(ct);
        var allergens = preferences.Where(x => !x.IsDislike).Select(x => FoodCodeNormalizer.Normalize(x.AllergenCode)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var disliked = preferences.Where(x => x.IsDislike && x.AllergenCode.StartsWith("INGREDIENT:", StringComparison.OrdinalIgnoreCase))
            .Select(x => Guid.TryParse(x.AllergenCode[11..], out var id) ? id : Guid.Empty).Where(x => x != Guid.Empty).ToHashSet();
        var dietary = await db.CustomerDietaryPreferences.AsNoTracking().Where(x => x.CustomerUserId == subscription.CustomerUserId).Select(x => x.PreferenceCode).ToListAsync(ct);
        var current = await db.DailyMealSelections.Where(x => x.SubscriptionDayId == day.Id).ToListAsync(ct);
        var safe = meals.Where(x =>
                !x.PresentAllergenCodes.Concat(x.MayContainAllergenCodes).Concat(x.Ingredients.SelectMany(i => i.AllergenCodes)).Any(allergens.Contains)
                && !x.Ingredients.Any(i => disliked.Contains(i.Id))
                && dietary.All(code => x.DietaryPreferenceCodes.Contains(code, StringComparer.OrdinalIgnoreCase)))
            .ToList();
        var unsafeSelections = current.Where(x => safe.All(meal => meal.MealId != x.MealId)).ToList();
        db.DailyMealSelections.RemoveRange(unsafeSelections);
        current.RemoveAll(unsafeSelections.Contains);
        var added = 0;
        foreach (var requirement in requirements)
        {
            var selected = current.Where(selection => safe.Any(meal => meal.MealId == selection.MealId && meal.Slot.Equals(requirement.Slot, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(x => x.SortOrder).ToList();
            foreach (var extra in selected.Skip(requirement.Count)) { db.DailyMealSelections.Remove(extra); current.Remove(extra); }
            var missing = requirement.Count - Math.Min(requirement.Count, selected.Count);
            var choices = safe.Where(meal => meal.Slot.Equals(requirement.Slot, StringComparison.OrdinalIgnoreCase) && current.All(selection => selection.MealId != meal.MealId))
                .OrderBy(x => x.SortOrder).Take(missing).ToList();
            foreach (var choice in choices)
            {
                var selection = new DailyMealSelection { SubscriptionDayId = day.Id, MealId = choice.MealId, SortOrder = current.Count + added++, SelectedAtUtc = now, ResolvedMenuPlanVersionId = day.ResolvedMenuPlanVersionId, ResolvedProgramId = subscription.ProgramId };
                db.DailyMealSelections.Add(selection);
                current.Add(selection);
            }
            if (choices.Count < missing)
            {
                day.Status = SubscriptionDayStatus.ReplacementRequired;
                day.LockAtUtc = null;
                return false;
            }
        }
        if (added > 0) day.IsAutoSelected = true;
        return true;
    }
}
