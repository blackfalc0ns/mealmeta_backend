using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MealMate.Persistence.Outbox;

public sealed class OutboxBackgroundJob : BackgroundService, IOutboxService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxBackgroundJob>? _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(2);

    public OutboxBackgroundJob(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxBackgroundJob>? logger = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessagesAsync(50, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger?.LogError(ex, "Unexpected error in OutboxBackgroundJob.");
            }

            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task<int> ProcessPendingMessagesAsync(CancellationToken cancellationToken = default)
        => await ProcessPendingMessagesAsync(50, cancellationToken);

    public async Task<int> ProcessPendingMessagesAsync(int batchSize = 50, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var nowUtc = DateTime.UtcNow;

        var messages = await db.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null &&
                        m.PoisonedAtUtc == null &&
                        (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= nowUtc))
            .OrderBy(m => m.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0) return 0;

        var processedCount = 0;

        foreach (var msg in messages)
        {
            msg.LastAttemptedAtUtc = nowUtc;

            try
            {
                var notification = new OutboxLifecycleNotification(
                    msg.Id,
                    msg.EventType,
                    msg.AggregateType,
                    msg.AggregateId,
                    msg.CorrelationId,
                    msg.DeduplicationKey,
                    msg.PayloadJson,
                    msg.CreatedAtUtc);

                await mediator.Publish(notification, cancellationToken);

                var legacyNotification = new OutboxMessageNotification(
                    msg.Id,
                    msg.EventType,
                    msg.PayloadJson,
                    msg.CreatedAtUtc);

                await mediator.Publish(legacyNotification, cancellationToken);

                msg.ProcessedAtUtc = nowUtc;
                msg.LastError = null;
                processedCount++;
            }
            catch (Exception ex)
            {
                msg.RetryCount++;
                msg.LastError = ex.Message;

                if (msg.RetryCount >= 5)
                {
                    msg.PoisonedAtUtc = nowUtc;
                    _logger?.LogError(ex, "Outbox message {Id} poisoned after {Retries} attempts.", msg.Id, msg.RetryCount);
                }
                else
                {
                    var delays = new[] { 1, 5, 15, 60, 300 };
                    var delaySec = delays[Math.Min(msg.RetryCount - 1, delays.Length - 1)];
                    msg.NextAttemptAtUtc = nowUtc.AddSeconds(delaySec);
                    _logger?.LogWarning(ex, "Outbox message {Id} failed attempt {Retries}. Next attempt in {Delay}s.", msg.Id, msg.RetryCount, delaySec);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return processedCount;
    }
}
