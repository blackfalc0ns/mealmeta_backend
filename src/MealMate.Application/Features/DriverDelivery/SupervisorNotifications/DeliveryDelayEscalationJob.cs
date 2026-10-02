using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.DriverDelivery.SupervisorNotifications;

public sealed class DeliveryDelayEscalationJob(IServiceScopeFactory scopes, ILogger<DeliveryDelayEscalationJob> logger)
    : ScheduledJob(scopes, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);
    protected override string JobName => nameof(DeliveryDelayEscalationJob);

    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var service = services.GetRequiredService<DeliveryDelayEscalationService>();
        await service.DetectAndNotifyAsync(cancellationToken);
    }
}
