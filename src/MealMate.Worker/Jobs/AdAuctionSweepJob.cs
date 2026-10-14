using MealMate.Application.Features.Advertising.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Worker.Jobs;

public sealed class AdAuctionSweepJob : ScheduledJob
{
    public AdAuctionSweepJob(
        IServiceScopeFactory scopeFactory,
        ILogger<AdAuctionSweepJob> logger)
        : base(scopeFactory, logger)
    {
    }

    protected override TimeSpan Interval => TimeSpan.FromMinutes(1);

    protected override string JobName => nameof(AdAuctionSweepJob);

    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var sweep = services.GetRequiredService<AdAuctionSweepService>();
        await sweep.SweepAsync(cancellationToken);
    }
}
