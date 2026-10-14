using System.Text.Json;
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
        var blockedActivationCount = 0;
        var overdueReviewCount = 0;

        // 1. Transition Scheduled -> Active when StartsAtUtc <= now AND the stored approval is still valid.
        //    Expiry of review time or a terms change after approval never auto-launches a campaign.
        var scheduledToActivate = await _context.Campaigns
            .Where(c => c.Status == CampaignStatus.Scheduled && c.StartsAtUtc <= now)
            .ToListAsync(cancellationToken);

        foreach (var campaign in scheduledToActivate)
        {
            var canActivate = campaign.CanActivate();
            if (canActivate.IsFailure)
            {
                blockedActivationCount++;

                var blockedDedupKey = $"campaign-launch-blocked:{campaign.Id}:v{campaign.CurrentSnapshotVersion}";
                var alreadyBlocked = await _context.OutboxMessages
                    .AnyAsync(o => o.DeduplicationKey == blockedDedupKey, cancellationToken);
                if (!alreadyBlocked)
                {
                    _context.OutboxMessages.Add(new Domain.Common.OutboxMessage
                    {
                        EventType = "CampaignLaunchBlocked",
                        DeduplicationKey = blockedDedupKey,
                        AggregateType = nameof(Campaign),
                        AggregateId = campaign.Id.ToString(),
                        PayloadJson = JsonSerializer.Serialize(new
                        {
                            CampaignId = campaign.Id,
                            campaign.CurrentSnapshotVersion,
                            campaign.ApprovedSnapshotVersion,
                            Reason = canActivate.Error
                        }),
                        CreatedAtUtc = now
                    });
                }

                _context.CampaignAuditLogs.Add(new CampaignAuditLog
                {
                    CampaignId = campaign.Id,
                    ActorId = SystemActorId,
                    Action = "CampaignLaunchBlocked",
                    JustificationReason = canActivate.Error,
                    CreatedAtUtc = now
                });
                continue;
            }

            var oldStatus = campaign.Status;
            campaign.TransitionTo(CampaignStatus.Active, SystemActorId, "Scheduled campaign start time reached with a valid approval.");

            _context.CampaignAuditLogs.Add(new CampaignAuditLog
            {
                CampaignId = campaign.Id,
                ActorId = SystemActorId,
                Action = "CampaignActivatedByScheduler",
                JustificationReason = "Scheduled campaign start time reached with a valid approval.",
                DetailsJson = $"{{\"PreviousStatus\":\"{oldStatus}\",\"NewStatus\":\"{CampaignStatus.Active}\",\"ApprovedSnapshotVersion\":{campaign.ApprovedSnapshotVersion}}}",
                CreatedAtUtc = now
            });

            activatedCount++;
        }

        // 2. Overdue reviews are surfaced once per snapshot version; never auto-approved.
        var overdueReviews = await _context.Campaigns
            .Where(c => c.Status == CampaignStatus.UnderReview
                        && c.ReviewEndsAtUtc != null
                        && c.ReviewEndsAtUtc < now)
            .ToListAsync(cancellationToken);

        foreach (var campaign in overdueReviews)
        {
            var dedupKey = $"campaign-review-overdue:{campaign.Id}:v{campaign.CurrentSnapshotVersion}";
            var alreadyQueued = await _context.OutboxMessages
                .AnyAsync(o => o.DeduplicationKey == dedupKey, cancellationToken);
            if (alreadyQueued)
            {
                continue;
            }

            overdueReviewCount++;
            _context.OutboxMessages.Add(new Domain.Common.OutboxMessage
            {
                EventType = "CampaignReviewOverdue",
                DeduplicationKey = dedupKey,
                AggregateType = nameof(Campaign),
                AggregateId = campaign.Id.ToString(),
                PayloadJson = JsonSerializer.Serialize(new
                {
                    CampaignId = campaign.Id,
                    campaign.NameAr,
                    campaign.NameEn,
                    campaign.CurrentSnapshotVersion,
                    ReviewEndedAtUtc = campaign.ReviewEndsAtUtc
                }),
                CreatedAtUtc = now
            });
        }

        // 3. Transition Active / Stopped -> Ended when EndsAtUtc <= now
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

        if (activatedCount > 0 || endedCount > 0 || blockedActivationCount > 0 || overdueReviewCount > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
            _logger?.LogInformation(
                "Campaign lifecycle transitions applied: {Activated} activated, {Ended} ended, {Blocked} launch-blocked, {Overdue} overdue reviews notified.",
                activatedCount, endedCount, blockedActivationCount, overdueReviewCount);
        }

        return new CampaignLifecycleResult(activatedCount, endedCount);
    }
}
