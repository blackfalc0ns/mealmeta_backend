using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerPush;
using MealMate.Application.Features.PrepDocuments;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.CustomerCheckout.BackgroundJobs;

/// <summary>
/// Sends the customer −24h delivery preparation push when the prep window opens.
/// </summary>
public sealed class CustomerDeliveryPrepPushJob(
    IServiceScopeFactory scopes,
    ILogger<CustomerDeliveryPrepPushJob> logger)
    : ScheduledJob(scopes, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);
    protected override string JobName => nameof(CustomerDeliveryPrepPushJob);

    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<IAppDbContext>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var push = services.GetRequiredService<ICustomerPushNotifier>();
        var now = clock.UtcNow;

        var candidates = await db.SubscriptionDays.AsNoTracking()
            .Where(d => d.RestaurantId != null
                        && (d.Status == SubscriptionDayStatus.ConfirmedByRestaurant
                            || d.Status == SubscriptionDayStatus.Preparing
                            || d.Status == SubscriptionDayStatus.OutForDelivery
                            || d.Status == SubscriptionDayStatus.Delivered))
            .Select(d => new
            {
                d.Id,
                d.SubscriptionId,
                d.ServiceDate,
                d.SelectionDeadlineUtc,
            })
            .ToListAsync(cancellationToken);

        var due = candidates
            .Where(d => PrepDocumentsGenerationEligibility.IsPrepWindowStarted(
                d.ServiceDate,
                d.SelectionDeadlineUtc,
                now))
            .ToList();

        if (due.Count == 0)
            return;

        var subscriptionIds = due.Select(x => x.SubscriptionId).Distinct().ToList();
        var customers = await db.Subscriptions.AsNoTracking()
            .Where(x => subscriptionIds.Contains(x.Id))
            .Select(x => new { x.Id, x.CustomerUserId })
            .ToDictionaryAsync(x => x.Id, x => x.CustomerUserId, cancellationToken);

        var sent = 0;
        foreach (var day in due)
        {
            if (!customers.TryGetValue(day.SubscriptionId, out var customerUserId))
                continue;

            var alreadySent = await db.CustomerPushDeliveryLogs.AsNoTracking()
                .AnyAsync(x => x.UserId == customerUserId
                               && x.EventKey == CustomerPushEventKeys.DeliveryPrep24h
                               && x.SubjectId == day.Id,
                    cancellationToken);
            if (alreadySent)
                continue;

            await push.NotifyDeliveryPrepReminderAsync(
                customerUserId,
                day.Id,
                day.ServiceDate,
                cancellationToken);
            sent++;
        }

        if (sent > 0)
            logger.LogInformation("Sent {Count} customer delivery prep push notifications.", sent);
    }
}
