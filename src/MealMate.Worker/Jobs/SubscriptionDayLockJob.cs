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
            if (!await db.DailyMealSelections.AnyAsync(x => x.SubscriptionDayId == day.Id, cancellationToken))
            {
                var programId = await db.Subscriptions.Where(x => x.Id == day.SubscriptionId).Select(x => x.ProgramId).FirstAsync(cancellationToken);
                var menuDays = await db.MenuDays.Where(x => x.MenuPlanVersionId == day.ResolvedMenuPlanVersionId).OrderBy(x => x.DayNumber).ToListAsync(cancellationToken);
                var version = await db.MenuPlanVersions.FirstAsync(x => x.Id == day.ResolvedMenuPlanVersionId, cancellationToken);
                if (menuDays.Count > 0)
                {
                    var menuDay = menuDays[Math.Abs(day.ServiceDate.DayNumber - version.EffectiveFrom.DayNumber) % menuDays.Count];
                    var meals = await (from link in db.MenuDayMeals join meal in db.Meals on link.MealId equals meal.Id
                                       where link.MenuDayId == menuDay.Id && link.ProgramId == programId && !link.IsDeleted && meal.IsAvailableForNewOrders && meal.ApprovalStatus == VerificationStatus.Approved
                                       orderby link.Slot, link.SortOrder select new { link.MealId, link.SortOrder }).ToListAsync(cancellationToken);
                    foreach (var meal in meals.GroupBy(x => x.SortOrder).Select(x => x.First()))
                        db.DailyMealSelections.Add(new DailyMealSelection { SubscriptionDayId = day.Id, MealId = meal.MealId, SortOrder = meal.SortOrder, SelectedAtUtc = now, ResolvedMenuPlanVersionId = day.ResolvedMenuPlanVersionId, ResolvedMenuDayId = menuDay.Id, ResolvedProgramId = programId });
                    day.IsAutoSelected = true;
                }
            }
            day.Status = SubscriptionDayStatus.Locked72h;
            day.LockAtUtc = now;
        }
        if (days.Count > 0) await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Locked {Count} subscription days.", days.Count);
    }
}
