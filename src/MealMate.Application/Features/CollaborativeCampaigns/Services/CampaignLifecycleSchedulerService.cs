using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public sealed class CampaignLifecycleSchedulerService : ICampaignLifecycleSchedulerService
{
    private const string SystemActorId = "SYSTEM_LIFECYCLE_SCHEDULER";
    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<CampaignLifecycleSchedulerService>? _logger;

    public CampaignLifecycleSchedulerService(
        IAppDbContext context,
        IDateTimeProvider clock,
        ILogger<CampaignLifecycleSchedulerService>? logger = null)
    {
        _context = context;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CampaignLifecycleResult> ProcessLifecycleTransitionsAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var activatedCount = 0;
        var endedCount = 0;

        // 1. Transition Scheduled -> Active when StartsAtUtc <= now
        var scheduledToActivate = await _context.Campaigns
            .Where(c => c.Status == CampaignStatus.Scheduled && c.StartsAtUtc <= now)
            .ToListAsync(cancellationToken);

        foreach (var campaign in scheduledToActivate)
        {
            var oldStatus = campaign.Status;
            campaign.TransitionTo(CampaignStatus.Active, SystemActorId, "Scheduled campaign start time reached.");

            _context.CampaignAuditLogs.Add(new CampaignAuditLog
            {
                CampaignId = campaign.Id,
                ActorId = SystemActorId,
                Action = "CampaignActivatedByScheduler",
                JustificationReason = "Scheduled campaign start time reached.",
                DetailsJson = $"{{\"PreviousStatus\":\"{oldStatus}\",\"NewStatus\":\"{CampaignStatus.Active}\"}}",
                CreatedAtUtc = now
            });

            activatedCount++;
        }

        // 2. Transition Active / Stopped -> Ended when EndsAtUtc <= now
        var toEnd = await _context.Campaigns
            .Where(c => (c.Status == CampaignStatus.Active || c.Status == CampaignStatus.Stopped) && c.EndsAtUtc <= now)
            .ToListAsync(cancellationToken);

        foreach (var campaign in toEnd)
        {
            var oldStatus = campaign.Status;
            campaign.TransitionTo(CampaignStatus.Ended, SystemActorId, "Campaign end time reached.");

            _context.CampaignAuditLogs.Add(new CampaignAuditLog
            {
                CampaignId = campaign.Id,
                ActorId = SystemActorId,
                Action = "CampaignEndedByScheduler",
                JustificationReason = "Campaign end time reached.",
                DetailsJson = $"{{\"PreviousStatus\":\"{oldStatus}\",\"NewStatus\":\"{CampaignStatus.Ended}\"}}",
                CreatedAtUtc = now
            });

            endedCount++;
        }

        if (activatedCount > 0 || endedCount > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
            _logger?.LogInformation(
                "Campaign lifecycle transitions applied: {Activated} activated, {Ended} ended.",
                activatedCount, endedCount);
        }

        return new CampaignLifecycleResult(activatedCount, endedCount);
    }
}
