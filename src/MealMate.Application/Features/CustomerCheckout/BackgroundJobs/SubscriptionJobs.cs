using MealMate.Application.Common.Abstractions;
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
            logger.LogInformation("Cancelled {SubscriptionCount} expired unpaid checkouts and released {HoldCount} holds.", result.SubscriptionsExpired, result.HoldsExpired);
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
                (x.Status == SubscriptionDayStatus.Open
                    || x.Status == SubscriptionDayStatus.AutoSelected
                    || x.Status == SubscriptionDayStatus.CustomerSelected
                    || x.Status == SubscriptionDayStatus.ReplacementRequired)
                && x.SelectionDeadlineUtc != null && x.SelectionDeadlineUtc <= now)
            .ToListAsync(cancellationToken);
        var engine = services.GetRequiredService<MealMate.Application.Features.CustomerCalendar.CustomerCalendarEngine>();
        foreach (var day in days)
            await engine.AutoSelectFullBoxAsync(day.SubscriptionId, day.ServiceDate, lockAfter: true, requireOwner: false, cancellationToken);
        if (days.Count > 0) await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Processed {Count} due subscription days.", days.Count);
    }
}
