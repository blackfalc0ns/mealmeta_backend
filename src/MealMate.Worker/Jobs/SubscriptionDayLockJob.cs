using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Worker.Jobs;

/// <summary>
/// Skeleton for the -72h lock job: locks customer editing, snapshots selections,
/// and sends the order to the restaurant. Business logic is intentionally not
/// implemented yet; this only establishes the scheduling/DI scaffolding.
/// </summary>
public sealed class SubscriptionDayLockJob : ScheduledJob
{
    private readonly ILogger<SubscriptionDayLockJob> _logger;

    public SubscriptionDayLockJob(
        IServiceScopeFactory scopeFactory,
        ILogger<SubscriptionDayLockJob> logger)
        : base(scopeFactory, logger)
        => _logger = logger;

    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);

    protected override string JobName => nameof(SubscriptionDayLockJob);

    protected override Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        // TODO: dispatch a MediatR command (e.g. LockDueSubscriptionDaysCommand) here.
        _logger.LogDebug("{JobName} tick — no work implemented yet.", JobName);
        return Task.CompletedTask;
    }
}
