using MealMate.Application.Features.CustomerCheckout;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Worker.Jobs;

public sealed class SubscriptionRestaurantPoolBackfillJob : ScheduledJob
{
    private readonly ILogger<SubscriptionRestaurantPoolBackfillJob> _logger;
    public SubscriptionRestaurantPoolBackfillJob(IServiceScopeFactory scopeFactory, ILogger<SubscriptionRestaurantPoolBackfillJob> logger)
        : base(scopeFactory, logger) => _logger = logger;
    protected override TimeSpan Interval => TimeSpan.FromMinutes(10);
    protected override string JobName => nameof(SubscriptionRestaurantPoolBackfillJob);

    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var result = await services.GetRequiredService<ISubscriptionRestaurantPoolBackfillService>().RunAsync(100, cancellationToken);
        if (result.Completed + result.Unresolved > 0)
            _logger.LogInformation("Restaurant pool backfill completed {Completed}; unresolved {Unresolved}.", result.Completed, result.Unresolved);
    }
}
