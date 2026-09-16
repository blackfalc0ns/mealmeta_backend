using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public sealed class OutboxDispatcherService : IOutboxDispatcherService
{
    private const int MaxRetries = 5;
    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly IMediator _mediator;
    private readonly ILogger<OutboxDispatcherService>? _logger;

    public OutboxDispatcherService(
        IAppDbContext context,
        IDateTimeProvider clock,
        IMediator mediator,
        ILogger<OutboxDispatcherService>? logger = null)
    {
        _context = context;
        _clock = clock;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<OutboxDispatchResult> DispatchPendingMessagesAsync(int batchSize = 50, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var processedCount = 0;
        var failedCount = 0;
        var deadLetteredCount = 0;

        var pendingMessages = await _context.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null && m.RetryCount < MaxRetries)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (pendingMessages.Count == 0)
        {
            return new OutboxDispatchResult(0, 0, 0);
        }

        var anyModified = false;

        foreach (var msg in pendingMessages)
        {
            // Check exponential backoff: 2^RetryCount seconds
            if (msg.RetryCount > 0 && msg.LastAttemptedAtUtc.HasValue)
            {
                var requiredDelay = TimeSpan.FromSeconds(Math.Pow(2, msg.RetryCount));
                if (now < msg.LastAttemptedAtUtc.Value + requiredDelay)
                {
                    continue;
                }
            }

            anyModified = true;
            msg.LastAttemptedAtUtc = now;

            try
            {
                var notification = new OutboxMessageNotification(
                    msg.Id,
                    msg.EventType,
                    msg.PayloadJson,
                    msg.CreatedAtUtc);

                await _mediator.Publish(notification, cancellationToken);

                msg.ProcessedAtUtc = now;
                msg.LastError = null;
                processedCount++;
            }
            catch (Exception ex)
            {
                msg.RetryCount++;
                msg.LastError = ex.Message;

                if (msg.RetryCount >= MaxRetries)
                {
                    deadLetteredCount++;
                    _logger?.LogError(ex, "Outbox message {Id} reached max retries ({Max}) and was dead-lettered.", msg.Id, MaxRetries);
                }
                else
                {
                    failedCount++;
                    _logger?.LogWarning(ex, "Outbox message {Id} failed attempt {Retry}. Will retry with backoff.", msg.Id, msg.RetryCount);
                }
            }
        }

        if (anyModified)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new OutboxDispatchResult(processedCount, failedCount, deadLetteredCount);
    }
}
