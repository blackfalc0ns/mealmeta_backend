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

        var overdueDays = await db.SubscriptionDays
            .Where(x => (x.Status == SubscriptionDayStatus.Locked72h || x.Status == SubscriptionDayStatus.SentToRestaurant)
                        && x.LockAtUtc != null
                        && x.LockAtUtc.Value.AddHours(24) <= now)
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
                Reason = $"DayId={day.Id}; ServiceDate={day.ServiceDate}; RestaurantId={day.RestaurantId}; Exceeded 24h confirmation window.",
                PerformedByUserId = Guid.Empty,
                PerformedByUserType = UserType.Admin,
                TimestampUtc = now
            });
        }

        if (overdueDays.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Escalated {Count} overdue restaurant confirmation days to ReplacementRequired.", overdueDays.Count);
        }
    }
}
