using MealMate.Application.Features.UnifiedSupport.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Worker.Jobs;

public sealed class SupportEvidenceCleanupJob : ScheduledJob
{
    private readonly ILogger<SupportEvidenceCleanupJob> _logger;
    private readonly TimeSpan _interval;

    public SupportEvidenceCleanupJob(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<SupportEvidenceCleanupJob> logger)
        : base(scopeFactory, logger)
    {
        _logger = logger;
        var intervalSeconds = configuration.GetValue<int?>("BackgroundJobs:SupportEvidenceCleanupIntervalSeconds") ?? 1800;
        if (intervalSeconds <= 0)
            throw new InvalidOperationException("BackgroundJobs:SupportEvidenceCleanupIntervalSeconds must be positive.");
        _interval = TimeSpan.FromSeconds(intervalSeconds);
    }

    protected override TimeSpan Interval => _interval;

    protected override string JobName => nameof(SupportEvidenceCleanupJob);

    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var cleanup = services.GetRequiredService<OrphanSupportEvidenceCleanupService>();
        var deletedCount = await cleanup.RunCleanupOnceAsync(cancellationToken);
        _logger.LogInformation("{JobName} completed with {DeletedCount} payload(s) removed.", JobName, deletedCount);
    }
}
