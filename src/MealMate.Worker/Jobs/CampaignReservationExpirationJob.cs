using MealMate.Application.Features.CollaborativeCampaigns.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Worker.Jobs;

public sealed class CampaignReservationExpirationJob : ScheduledJob
{
    private readonly ILogger<CampaignReservationExpirationJob> _logger;

    public CampaignReservationExpirationJob(
        IServiceScopeFactory scopeFactory,
        ILogger<CampaignReservationExpirationJob> logger)
        : base(scopeFactory, logger)
    {
        _logger = logger;
    }

    protected override TimeSpan Interval => TimeSpan.FromMinutes(1);

    protected override string JobName => nameof(CampaignReservationExpirationJob);

    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var service = services.GetRequiredService<ICampaignReservationExpirationService>();
        var expiredCount = await service.ExpireStaleReservationsAsync(cancellationToken);

        if (expiredCount > 0)
        {
            _logger.LogInformation("{JobName} expired {Count} stale campaign checkout reservation(s).", JobName, expiredCount);
        }
    }
}
