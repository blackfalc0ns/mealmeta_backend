using MealMate.Application.Features.Offers.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Worker.Jobs;

public sealed class OfferLifecycleSchedulerJob : ScheduledJob
{
    private readonly ILogger<OfferLifecycleSchedulerJob> _logger;

    public OfferLifecycleSchedulerJob(
        IServiceScopeFactory scopeFactory,
        ILogger<OfferLifecycleSchedulerJob> logger)
        : base(scopeFactory, logger)
    {
        _logger = logger;
    }

    protected override TimeSpan Interval => TimeSpan.FromMinutes(1);

    protected override string JobName => nameof(OfferLifecycleSchedulerJob);

    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var service = services.GetRequiredService<IOfferLifecycleSchedulerService>();
        var result = await service.ProcessLifecycleTransitionsAsync(cancellationToken);

        if (result.ActivatedCount > 0 || result.EndedCount > 0)
        {
            _logger.LogInformation(
                "{JobName} processed offer lifecycle transitions: {Activated} activated, {Ended} ended.",
                JobName, result.ActivatedCount, result.EndedCount);
        }
    }
}
