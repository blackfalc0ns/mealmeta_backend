using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Worker.Jobs;

public sealed class CapacityHoldExpiryJob : ScheduledJob
{
    private readonly ILogger<CapacityHoldExpiryJob> _logger;
    public CapacityHoldExpiryJob(IServiceScopeFactory scopeFactory, ILogger<CapacityHoldExpiryJob> logger) : base(scopeFactory, logger) => _logger = logger;
    protected override TimeSpan Interval => TimeSpan.FromMinutes(1);
    protected override string JobName => nameof(CapacityHoldExpiryJob);
    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var expiry = services.GetRequiredService<ICapacityHoldExpiryService>();
        var result = await expiry.ExpireDueAsync(cancellationToken);
        if (result.HoldsExpired > 0)
            _logger.LogInformation("Expired {HoldCount} checkout capacity holds and {SubscriptionCount} unpaid subscriptions.", result.HoldsExpired, result.SubscriptionsExpired);
    }
}
