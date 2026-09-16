using MealMate.Application.Features.CollaborativeCampaigns.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Worker.Jobs;

public sealed class CampaignLifecycleSchedulerJob : ScheduledJob
{
    private readonly ILogger<CampaignLifecycleSchedulerJob> _logger;

    public CampaignLifecycleSchedulerJob(
        IServiceScopeFactory scopeFactory,
        ILogger<CampaignLifecycleSchedulerJob> logger)
        : base(scopeFactory, logger)
    {
        _logger = logger;
    }

    protected override TimeSpan Interval => TimeSpan.FromMinutes(1);

    protected override string JobName => nameof(CampaignLifecycleSchedulerJob);

    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var service = services.GetRequiredService<ICampaignLifecycleSchedulerService>();
        var result = await service.ProcessLifecycleTransitionsAsync(cancellationToken);

        if (result.ActivatedCount > 0 || result.EndedCount > 0)
        {
            _logger.LogInformation(
                "{JobName} processed lifecycle transitions: {Activated} activated, {Ended} ended.",
                JobName, result.ActivatedCount, result.EndedCount);
        }
    }
}
