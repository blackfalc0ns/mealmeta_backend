using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Worker.Jobs;

public sealed class SubscriptionFulfillmentRetryJob : ScheduledJob
{
    private readonly ILogger<SubscriptionFulfillmentRetryJob> _logger;
    public SubscriptionFulfillmentRetryJob(IServiceScopeFactory scopeFactory, ILogger<SubscriptionFulfillmentRetryJob> logger)
        : base(scopeFactory, logger) => _logger = logger;

    protected override TimeSpan Interval => TimeSpan.FromMinutes(1);
    protected override string JobName => nameof(SubscriptionFulfillmentRetryJob);

    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var candidates = await db.Subscriptions.AsNoTracking()
            .Where(x => x.Status == SubscriptionStatus.Paid && x.FulfillmentCompletedAtUtc == null)
            .OrderBy(x => x.LastFulfillmentAttemptUtc)
            .Take(100)
            .Select(x => new { x.Id, x.FulfillmentAttemptCount, x.LastFulfillmentAttemptUtc })
            .ToListAsync(cancellationToken);
        var service = services.GetRequiredService<ISubscriptionFulfillmentService>();
        var attempted = 0;
        foreach (var candidate in candidates)
        {
            var delayMinutes = Math.Min(30, Math.Pow(2, Math.Max(0, candidate.FulfillmentAttemptCount - 1)));
            if (candidate.LastFulfillmentAttemptUtc.HasValue
                && candidate.LastFulfillmentAttemptUtc.Value.AddMinutes(delayMinutes) > now)
                continue;
            await service.TryFulfillAsync(candidate.Id, cancellationToken);
            attempted++;
        }
        if (attempted > 0)
            _logger.LogInformation("Retried fulfillment for {SubscriptionCount} paid subscriptions.", attempted);
    }
}
