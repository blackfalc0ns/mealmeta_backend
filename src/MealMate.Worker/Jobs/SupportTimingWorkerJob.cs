using MealMate.Application.Features.UnifiedSupport.Timing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Worker.Jobs;

public sealed class SupportTimingWorkerJob : ScheduledJob
{
    private readonly ILogger<SupportTimingWorkerJob> _logger;

    public SupportTimingWorkerJob(
        IServiceScopeFactory scopeFactory,
        ILogger<SupportTimingWorkerJob> logger)
        : base(scopeFactory, logger)
    {
        _logger = logger;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(30);

    protected override string JobName => nameof(SupportTimingWorkerJob);

    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var processor = services.GetRequiredService<ISupportTimingProcessor>();
        var result = await processor.ProcessTimingTickAsync(cancellationToken);

        if (result.FirstResponseBreachesCount > 0 || result.ResolutionBreachesCount > 0 || result.EscalatedCount > 0 || result.UnroutedCount > 0)
        {
            _logger.LogInformation(
                "{JobName} processed {Processed} cases: {FR} FR breaches, {Res} Res breaches, {Esc} escalated, {Unr} unrouted.",
                JobName, result.ProcessedCasesCount, result.FirstResponseBreachesCount, result.ResolutionBreachesCount, result.EscalatedCount, result.UnroutedCount);
        }
    }
}
