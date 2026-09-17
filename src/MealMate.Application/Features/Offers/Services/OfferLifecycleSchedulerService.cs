using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.Offers.Services;

public sealed class OfferLifecycleSchedulerService : IOfferLifecycleSchedulerService
{
    private const string SystemActorId = "SYSTEM_OFFER_LIFECYCLE_SCHEDULER";
    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<OfferLifecycleSchedulerService>? _logger;

    public OfferLifecycleSchedulerService(
        IAppDbContext context,
        IDateTimeProvider clock,
        ILogger<OfferLifecycleSchedulerService>? logger = null)
    {
        _context = context;
        _clock = clock;
        _logger = logger;
    }

    public async Task<OfferLifecycleResult> ProcessLifecycleTransitionsAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var activatedCount = 0;
        var endedCount = 0;

        // 1. Transition Scheduled -> Active when StartsAtUtc <= now
        var scheduledToActivate = await _context.Offers
            .Where(o => o.Status == OfferStatus.Scheduled && o.StartsAtUtc <= now)
            .ToListAsync(cancellationToken);

        foreach (var offer in scheduledToActivate)
        {
            var oldStatus = offer.Status;
            offer.TransitionTo(OfferStatus.Active, SystemActorId, "Scheduled offer start time reached.");

            _context.OfferAuditLogs.Add(new OfferAuditLog
            {
                OfferId = offer.Id,
                ActorId = SystemActorId,
                Action = "OfferActivatedByScheduler",
                JustificationReason = "Scheduled offer start time reached.",
                DetailsJson = $"{{\"PreviousStatus\":\"{oldStatus}\",\"NewStatus\":\"{OfferStatus.Active}\"}}",
                CreatedAtUtc = now
            });

            activatedCount++;
        }

        // 2. Transition Active / Paused / Stopped -> Ended when EndsAtUtc <= now
        var toEnd = await _context.Offers
            .Where(o => (o.Status == OfferStatus.Active || o.Status == OfferStatus.Paused || o.Status == OfferStatus.Stopped) && o.EndsAtUtc <= now)
            .ToListAsync(cancellationToken);

        foreach (var offer in toEnd)
        {
            var oldStatus = offer.Status;
            offer.TransitionTo(OfferStatus.Ended, SystemActorId, "Offer validity period ended.");

            _context.OfferAuditLogs.Add(new OfferAuditLog
            {
                OfferId = offer.Id,
                ActorId = SystemActorId,
                Action = "OfferEndedByScheduler",
                JustificationReason = "Offer validity period ended.",
                DetailsJson = $"{{\"PreviousStatus\":\"{oldStatus}\",\"NewStatus\":\"{OfferStatus.Ended}\"}}",
                CreatedAtUtc = now
            });

            endedCount++;
        }

        if (activatedCount > 0 || endedCount > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
            _logger?.LogInformation(
                "Offer lifecycle transitions applied: {Activated} activated, {Ended} ended.",
                activatedCount, endedCount);
        }

        return new OfferLifecycleResult(activatedCount, endedCount);
    }
}
