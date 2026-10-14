using MealMate.Application.Features.Advertising.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Worker.Jobs;

public sealed class AdPaymentReconciliationJob : ScheduledJob
{
    public AdPaymentReconciliationJob(
        IServiceScopeFactory scopeFactory,
        ILogger<AdPaymentReconciliationJob> logger)
        : base(scopeFactory, logger)
    {
    }

    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);

    protected override string JobName => nameof(AdPaymentReconciliationJob);

    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var reconciliation = services.GetRequiredService<AdPaymentReconciliationService>();
        await reconciliation.ProcessPendingPaymentsAsync(cancellationToken);
        await reconciliation.ProcessPendingRefundsAsync(cancellationToken);
    }
}
