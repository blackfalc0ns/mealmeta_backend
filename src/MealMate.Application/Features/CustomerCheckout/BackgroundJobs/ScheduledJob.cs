using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.CustomerCheckout.BackgroundJobs;

public abstract class ScheduledJob(IServiceScopeFactory scopeFactory, ILogger logger) : BackgroundService
{
    protected abstract TimeSpan Interval { get; }
    protected abstract string JobName { get; }
    protected abstract Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("{JobName} started with interval {Interval}.", JobName, Interval);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await ExecuteOnceAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "{JobName} tick failed.", JobName);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
