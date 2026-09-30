using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using Microsoft.Extensions.Hosting;

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

public sealed record SubscriptionDayLockResult(
    int DueCount,
    int SucceededCount,
    int FailedCount,
    int EscalatedCount);

public sealed class SubscriptionDayLockJob(IServiceScopeFactory scopes, ILogger<SubscriptionDayLockJob> logger)
    : ScheduledJob(scopes, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromSeconds(15);
    protected override string JobName => nameof(SubscriptionDayLockJob);

    public async Task<SubscriptionDayLockResult> ProcessDueDaysAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<IAppDbContext>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var now = clock.UtcNow;

        var candidates = await db.SubscriptionDays.AsNoTracking().Where(x =>
                (x.Status == SubscriptionDayStatus.Open
                    || x.Status == SubscriptionDayStatus.AutoSelected
                    || x.Status == SubscriptionDayStatus.CustomerSelected
                    || x.Status == SubscriptionDayStatus.ReplacementRequired)
                && x.LockAtUtc == null)
            .ToListAsync(cancellationToken);

        var policies = services.GetService<IOperationalPolicyService>();
        var environment = services.GetService<IHostEnvironment>();
        if (policies is not null) await OperationalDeadlineRules.RefreshAsync(db, candidates, policies,
            environment?.IsDevelopment() == true || environment?.IsEnvironment("Test") == true, cancellationToken);
        var settings = policies is not null ? await policies.GetSettingsAsync(cancellationToken) : await OperationalPolicyRules.ReadAsync(db, cancellationToken);
        var days = candidates.Where(x => x.SelectionDeadlineUtc != null && x.SelectionDeadlineUtc <= now).ToList();

        var engine = services.GetRequiredService<MealMate.Application.Features.CustomerCalendar.CustomerCalendarEngine>();

        if (settings.AutoSelection?.Enabled != false && settings.AutoSelection?.MinutesBeforeCutoff is > 0)
        {
            foreach (var candidate in candidates.Where(x => x.Status == SubscriptionDayStatus.Open && x.SelectionDeadlineUtc > now &&
                         x.SelectionDeadlineUtc?.AddMinutes(-settings.AutoSelection.MinutesBeforeCutoff) <= now))
            {
                var selection = await engine.AutoSelectFullBoxAsync(candidate.SubscriptionId, candidate.ServiceDate, false, false, cancellationToken, scheduledAutomatic: true);
                if (selection.IsFailure) logger.LogWarning("Early automatic selection for day {DayId} failed: {Error}", candidate.Id, selection.Error.Code);
            }
        }

        var succeeded = 0;
        var failed = 0;
        var escalated = 0;

        foreach (var day in days)
        {
            try
            {
                var result = await engine.AutoSelectFullBoxAsync(
                    day.SubscriptionId, day.ServiceDate, lockAfter: true, requireOwner: false, cancellationToken);

                if (result.IsSuccess)
                {
                    succeeded++;
                    logger.LogInformation("Successfully locked SubscriptionDay {SubscriptionDayId} for Subscription {SubscriptionId} on {ServiceDate}.",
                        day.Id, day.SubscriptionId, day.ServiceDate);
                }
                else
                {
                    failed++;
                    logger.LogWarning("Failed to lock SubscriptionDay {SubscriptionDayId} on {ServiceDate}: {ErrorCode} - {ErrorDescription}",
                        day.Id, day.ServiceDate, result.Error.Code, result.Error.Description);

                    if (result.Error.Code == MessageCodes.Calendar.NoSafeBox || result.Error.Code == "NO_SAFE_MEALS_FOUND")
                    {
                        var transactions = services.GetService<IApplicationTransactionExecutor>();
                        var locks = services.GetService<IApplicationLockManager>();
                        async Task<bool> Escalate(CancellationToken token)
                        {
                        if (locks is not null) await locks.LockSubscriptionAsync(day.SubscriptionId, token);
                        var currentDay = await db.SubscriptionDays.FirstOrDefaultAsync(x => x.Id == day.Id, token);
                        if (currentDay is null || !OperationalDeadlineRules.IsMutable(currentDay)) return false;
                        if (policies is not null) await OperationalDeadlineRules.RefreshAsync(db, new[] { currentDay }, policies,
                            environment?.IsDevelopment() == true || environment?.IsEnvironment("Test") == true, token);
                        if (currentDay.SelectionDeadlineUtc is null || currentDay.SelectionDeadlineUtc > clock.UtcNow) return false;
                        currentDay.MarkReplacementRequired(result.Error.Description ?? "No safe meals found for auto-selection");
                        var eventType = "CustomerReplacementRequired";
                        var dayIdStr = day.Id.ToString();
                        var exists = await db.OutboxMessages.AnyAsync(m => m.EventType == eventType && m.PayloadJson.Contains(dayIdStr), cancellationToken);
                        if (!exists)
                        {
                            var payload = JsonSerializer.Serialize(new
                            {
                                SubscriptionDayId = day.Id,
                                SubscriptionId = day.SubscriptionId,
                                ServiceDate = day.ServiceDate,
                                Reason = result.Error.Description ?? "No safe meals found for auto-selection",
                                EscalatedAtUtc = now
                            });

                            db.OutboxMessages.Add(new OutboxMessage
                            {
                                EventType = eventType,
                                PayloadJson = payload,
                                CreatedAtUtc = now
                            });
                            await db.SaveChangesAsync(token);
                            return true;
                        }
                        await db.SaveChangesAsync(token);
                        return false;
                        }
                        if (transactions is not null ? await transactions.ExecuteAsync(Escalate, cancellationToken) : await Escalate(cancellationToken)) escalated++;
                    }
                }
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogError(ex, "Unexpected error locking SubscriptionDay {SubscriptionDayId} on {ServiceDate}.", day.Id, day.ServiceDate);
            }
        }

        if (days.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("SubscriptionDayLockJob completed: Total due {DueCount}, Succeeded {Succeeded}, Failed {Failed}, Escalated {Escalated}.",
            days.Count, succeeded, failed, escalated);

        return new SubscriptionDayLockResult(days.Count, succeeded, failed, escalated);
    }

    protected override Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
        => ProcessDueDaysAsync(services, cancellationToken);
}
