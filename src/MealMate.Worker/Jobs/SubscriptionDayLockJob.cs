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
                var requiredSlots = JsonSerializer.Deserialize<List<string>>(snapshot.RequiredSlotsJson) ?? [];
                var preferences = await db.CustomerAllergyPreferences.AsNoTracking().Where(x => x.CustomerUserId == subscription.CustomerUserId).ToListAsync(cancellationToken);
                var allergens = preferences.Where(x => !x.IsDislike).Select(x => FoodCodeNormalizer.Normalize(x.AllergenCode)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var dislikedIds = preferences.Where(x => x.IsDislike && x.AllergenCode.StartsWith("INGREDIENT:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(x.AllergenCode[11..], out _)).Select(x => Guid.Parse(x.AllergenCode[11..])).ToHashSet();
                var dietary = await db.CustomerDietaryPreferences.AsNoTracking().Where(x => x.CustomerUserId == subscription.CustomerUserId).Select(x => x.PreferenceCode).ToListAsync(cancellationToken);
                var current = await db.DailyMealSelections.Where(x => x.SubscriptionDayId == day.Id).ToListAsync(cancellationToken);
                var selectedSlots = current.Join(meals, x => x.MealId, x => x.MealId, (_, meal) => meal.Slot).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var added = 0;
                foreach (var slot in requiredSlots.Where(x => !selectedSlots.Contains(x)))
                {
                    var choice = meals.Where(x => string.Equals(x.Slot, slot, StringComparison.OrdinalIgnoreCase))
                        .Where(x => !x.PresentAllergenCodes.Concat(x.MayContainAllergenCodes).Concat(x.Ingredients.SelectMany(i => i.AllergenCodes)).Any(allergens.Contains))
                        .Where(x => !x.Ingredients.Any(i => dislikedIds.Contains(i.Id)))
                        .Where(x => dietary.All(code => x.DietaryPreferenceCodes.Contains(code, StringComparer.OrdinalIgnoreCase)))
                        .OrderBy(x => x.SortOrder).FirstOrDefault();
                    if (choice is null) continue;
                    db.DailyMealSelections.Add(new DailyMealSelection { SubscriptionDayId = day.Id, MealId = choice.MealId, SortOrder = current.Count + added++, SelectedAtUtc = now, ResolvedMenuPlanVersionId = day.ResolvedMenuPlanVersionId, ResolvedProgramId = subscription.ProgramId });
                }
                if (added > 0) day.IsAutoSelected = true;
            }
            day.Status = SubscriptionDayStatus.Locked72h;
            day.LockAtUtc = now;
        }
        if (days.Count > 0) await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Locked {Count} subscription days.", days.Count);
    }
}
