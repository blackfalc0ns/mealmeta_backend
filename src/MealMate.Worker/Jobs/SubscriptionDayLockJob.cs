using System.Text.Json;
using MealMate.Application.Common;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Worker.Jobs;

/// <summary>Locks due days and fills any missing customer choices from the pinned menu version.</summary>
public sealed class SubscriptionDayLockJob : ScheduledJob
{
    private readonly ILogger<SubscriptionDayLockJob> _logger;
    public SubscriptionDayLockJob(IServiceScopeFactory scopeFactory, ILogger<SubscriptionDayLockJob> logger) : base(scopeFactory, logger) => _logger = logger;
    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);
    protected override string JobName => nameof(SubscriptionDayLockJob);

    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var days = await db.SubscriptionDays.Where(x => (x.Status == SubscriptionDayStatus.Open || x.Status == SubscriptionDayStatus.AutoSelected || x.Status == SubscriptionDayStatus.CustomerSelected) && x.SelectionDeadlineUtc != null && x.SelectionDeadlineUtc <= now && x.ResolvedMenuPlanVersionId != null).ToListAsync(cancellationToken);
        foreach (var day in days)
        {
            var snapshot = await db.SubscriptionDayMealSnapshots.AsNoTracking().FirstOrDefaultAsync(x => x.SubscriptionDayId == day.Id, cancellationToken);
            if (snapshot is not null)
            {
                var subscription = await db.Subscriptions.AsNoTracking().Where(x => x.Id == day.SubscriptionId).Select(x => new { x.ProgramId, x.CustomerUserId }).FirstAsync(cancellationToken);
                var meals = JsonSerializer.Deserialize<List<MealSnapshotItem>>(snapshot.MealsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
                var requiredSlots = SlotRequirementJson.Parse(snapshot.RequiredSlotsJson);
                var preferences = await db.CustomerAllergyPreferences.AsNoTracking().Where(x => x.CustomerUserId == subscription.CustomerUserId).ToListAsync(cancellationToken);
                var allergens = preferences.Where(x => !x.IsDislike).Select(x => FoodCodeNormalizer.Normalize(x.AllergenCode)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var dislikedIds = preferences.Where(x => x.IsDislike && x.AllergenCode.StartsWith("INGREDIENT:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(x.AllergenCode[11..], out _)).Select(x => Guid.Parse(x.AllergenCode[11..])).ToHashSet();
                var dietary = await db.CustomerDietaryPreferences.AsNoTracking().Where(x => x.CustomerUserId == subscription.CustomerUserId).Select(x => x.PreferenceCode).ToListAsync(cancellationToken);
                var current = await db.DailyMealSelections.Where(x => x.SubscriptionDayId == day.Id).ToListAsync(cancellationToken);
                var safeMeals = meals.Where(x =>
                        !x.PresentAllergenCodes.Concat(x.MayContainAllergenCodes).Concat(x.Ingredients.SelectMany(i => i.AllergenCodes)).Any(allergens.Contains)
                        && !x.Ingredients.Any(i => dislikedIds.Contains(i.Id))
                        && dietary.All(code => x.DietaryPreferenceCodes.Contains(code, StringComparer.OrdinalIgnoreCase)))
                    .ToList();
                var safeIds = safeMeals.Select(x => x.MealId).ToHashSet();
                var unsafeSelections = current.Where(x => !safeIds.Contains(x.MealId)).ToList();
                if (unsafeSelections.Count > 0)
                {
                    db.DailyMealSelections.RemoveRange(unsafeSelections);
                    current = current.Except(unsafeSelections).ToList();
                }
                var added = 0;
                var incomplete = false;
                foreach (var requirement in requiredSlots)
                {
                    var selectedForSlot = current.Join(safeMeals.Where(x => string.Equals(x.Slot, requirement.Slot, StringComparison.OrdinalIgnoreCase)), x => x.MealId, x => x.MealId, (selection, _) => selection).OrderBy(x => x.SortOrder).ToList();
                    foreach (var extra in selectedForSlot.Skip(requirement.Count))
                    {
                        db.DailyMealSelections.Remove(extra);
                        current.Remove(extra);
                    }
                    var missing = requirement.Count - Math.Min(requirement.Count, selectedForSlot.Count);
                    var choices = safeMeals.Where(x => string.Equals(x.Slot, requirement.Slot, StringComparison.OrdinalIgnoreCase) && current.All(s => s.MealId != x.MealId))
                        .OrderBy(x => x.SortOrder).Take(missing).ToList();
                    foreach (var choice in choices)
                    {
                        var selection = new DailyMealSelection { SubscriptionDayId = day.Id, MealId = choice.MealId, SortOrder = current.Count + added++, SelectedAtUtc = now, ResolvedMenuPlanVersionId = day.ResolvedMenuPlanVersionId, ResolvedProgramId = subscription.ProgramId };
                        db.DailyMealSelections.Add(selection);
                        current.Add(selection);
                    }
                    if (choices.Count < missing) incomplete = true;
                }
                if (added > 0) day.IsAutoSelected = true;
                if (incomplete)
                {
                    day.Status = SubscriptionDayStatus.ReplacementRequired;
                    day.LockAtUtc = null;
                    continue;
                }
            }
            day.Status = SubscriptionDayStatus.Locked72h;
            day.LockAtUtc = now;
        }
        if (days.Count > 0) await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Locked {Count} subscription days.", days.Count);
    }
}
