using MealMate.Application.Features.CollaborativeCampaigns.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Worker.Jobs;

public sealed class CampaignCheckoutReconciliationJob : ScheduledJob
{
    private readonly ILogger<CampaignCheckoutReconciliationJob> _logger;

    public CampaignCheckoutReconciliationJob(
        IServiceScopeFactory scopeFactory,
        ILogger<CampaignCheckoutReconciliationJob> logger)
        : base(scopeFactory, logger)
    {
        _logger = logger;
    }

    protected override TimeSpan Interval => TimeSpan.FromMinutes(2);

    protected override string JobName => nameof(CampaignCheckoutReconciliationJob);

    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var service = services.GetRequiredService<ICampaignCheckoutReconciliationService>();
        var count = await service.ReconcileStuckSessionsAsync(cancellationToken);

        if (count > 0)
        {
            _logger.LogInformation("{JobName} reconciled {Count} stuck campaign checkout session(s).", JobName, count);
        }
    }
}
