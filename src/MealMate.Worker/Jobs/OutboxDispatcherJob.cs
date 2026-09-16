using MealMate.Application.Features.CollaborativeCampaigns.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Worker.Jobs;

public sealed class OutboxDispatcherJob : ScheduledJob
{
    private readonly ILogger<OutboxDispatcherJob> _logger;

    public OutboxDispatcherJob(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxDispatcherJob> logger)
        : base(scopeFactory, logger)
    {
        _logger = logger;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(5);

    protected override string JobName => nameof(OutboxDispatcherJob);

    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var dispatcher = services.GetRequiredService<IOutboxDispatcherService>();
        var result = await dispatcher.DispatchPendingMessagesAsync(cancellationToken: cancellationToken);

        if (result.ProcessedCount > 0 || result.DeadLetteredCount > 0)
        {
            _logger.LogInformation(
                "{JobName} processed {Processed} outbox message(s), {Failed} failed, {DeadLettered} dead-lettered.",
                JobName, result.ProcessedCount, result.FailedCount, result.DeadLetteredCount);
        }
    }
}
