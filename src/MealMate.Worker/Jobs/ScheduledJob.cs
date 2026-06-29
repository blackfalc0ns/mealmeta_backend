using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MealMate.Worker.Jobs;

/// <summary>
/// Base for periodic background jobs. Runs <see cref="ExecuteOnceAsync"/> on a fixed
/// interval, creating a fresh DI scope per tick so scoped services (DbContext) are
/// resolved correctly. Implementations must be idempotent and safe to rerun.
/// </summary>
public abstract class ScheduledJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;

    protected ScheduledJob(IServiceScopeFactory scopeFactory, ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected abstract TimeSpan Interval { get; }

    protected abstract string JobName { get; }

    protected abstract Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("{JobName} started with interval {Interval}.", JobName, Interval);

        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await ExecuteOnceAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{JobName} tick failed.", JobName);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
