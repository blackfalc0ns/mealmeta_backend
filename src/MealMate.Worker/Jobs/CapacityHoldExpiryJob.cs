using MealMate.Domain.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Worker.Jobs;

public sealed class CapacityHoldExpiryJob : ScheduledJob
{
    private readonly ILogger<CapacityHoldExpiryJob> _logger;
    public CapacityHoldExpiryJob(IServiceScopeFactory scopeFactory, ILogger<CapacityHoldExpiryJob> logger) : base(scopeFactory, logger) => _logger = logger;
    protected override TimeSpan Interval => TimeSpan.FromMinutes(1);
    protected override string JobName => nameof(CapacityHoldExpiryJob);
    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var holds = await db.CapacityHolds.Where(x => x.Status == CapacityHoldStatus.PendingPayment && x.ExpiresAtUtc <= now).ToListAsync(cancellationToken);
        if (holds.Count == 0) return;
        var ids = holds.Select(x => x.SubscriptionId).Distinct().ToList();
        foreach (var hold in holds) hold.Status = CapacityHoldStatus.Expired;
        var subscriptions = await db.Subscriptions.Where(x => ids.Contains(x.Id) && x.Status == SubscriptionStatus.PendingPayment).ToListAsync(cancellationToken);
        foreach (var subscription in subscriptions) { subscription.Status = SubscriptionStatus.Expired; subscription.LastOperationAtUtc = now; }
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Expired {HoldCount} checkout capacity holds and {SubscriptionCount} unpaid subscriptions.", holds.Count, subscriptions.Count);
    }
}
