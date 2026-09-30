using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.CustomerCheckout.BackgroundJobs;

public sealed class RestaurantOrderEscalationJob(IServiceScopeFactory scopes, ILogger<RestaurantOrderEscalationJob> logger)
    : ScheduledJob(scopes, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);
    protected override string JobName => nameof(RestaurantOrderEscalationJob);

    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<IAppDbContext>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var now = clock.UtcNow;
        var confirmationMinutes = (await MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalPolicyRules.ReadAsync(db, cancellationToken)).Cutoff.EffectiveConfirmationMinutes;

        var overdueDays = await db.SubscriptionDays
            .Where(x => (x.Status == SubscriptionDayStatus.Locked72h || x.Status == SubscriptionDayStatus.SentToRestaurant)
                        && db.RestaurantDailyOrders.Any(o => o.SubscriptionDayId == x.Id &&
                            o.RestaurantId == x.RestaurantId && o.Status == OrderStatus.AwaitingRestaurantConfirmation && o.CreatedAtUtc.AddMinutes(confirmationMinutes) <= now))
            .ToListAsync(cancellationToken);

        foreach (var day in overdueDays)
        {
            day.Status = SubscriptionDayStatus.ReplacementRequired;
            db.SubscriptionOperations.Add(new SubscriptionOperation
            {
                SubscriptionId = day.SubscriptionId,
                OperationType = "ORDER_CONFIRMATION_OVERDUE_ESCALATED",
                PreviousStatus = SubscriptionStatus.Active,
                NewStatus = SubscriptionStatus.Active,
                Reason = $"DayId={day.Id}; ServiceDate={day.ServiceDate}; RestaurantId={day.RestaurantId}; Exceeded {confirmationMinutes} minute confirmation window.",
                PerformedByUserId = Guid.Empty,
                PerformedByUserType = UserType.Admin,
                TimestampUtc = now
            });
        }

        var replacementOrchestrator = services.GetService<IOrderReplacementOrchestrator>();

        if (overdueDays.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Escalated {Count} overdue restaurant confirmation days to ReplacementRequired.", overdueDays.Count);

            if (replacementOrchestrator != null)
            {
                foreach (var day in overdueDays)
                {
                    try
                    {
                        var replaceResult = await replacementOrchestrator.AttemptAutoReplacementAsync(day.Id, cancellationToken);
                        logger.LogInformation("Auto replacement result for day {DayId}: {Status}, NewRestaurant={NewRestaurantId}",
                            day.Id, replaceResult.Status, replaceResult.NewRestaurantId);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to auto-replace overdue order for day {DayId}", day.Id);
                    }
                }
            }
        }
    }
}

