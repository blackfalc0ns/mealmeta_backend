using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.VoiceCalls.Commands;
using MealMate.Application.Features.VoiceCalls.Options;
using MealMate.Domain.VoiceCalls;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MealMate.Application.Features.VoiceCalls.BackgroundJobs;

/// <summary>
/// Background worker that queries for ringing VoIP calls exceeding the configured ring timeout
/// and transitions them to Missed status with a NoAnswer reason.
/// </summary>
public sealed class MissedCallTimeoutJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<VoiceCallOptions> _options;
    private readonly ILogger<MissedCallTimeoutJob> _logger;

    public MissedCallTimeoutJob(
        IServiceScopeFactory scopeFactory,
        IOptions<VoiceCallOptions> options,
        ILogger<MissedCallTimeoutJob> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = Math.Max(1, _options.Value.TimeoutWorkerIntervalSeconds);
        var interval = TimeSpan.FromSeconds(intervalSeconds);

        _logger.LogInformation("MissedCallTimeoutJob started with interval {IntervalSeconds}s and timeout {TimeoutSeconds}s.",
            intervalSeconds, _options.Value.RingTimeoutSeconds);

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredCallsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in MissedCallTimeoutJob cycle.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task<int> ProcessExpiredCallsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var timeoutSeconds = Math.Max(5, _options.Value.RingTimeoutSeconds);
        var cutoff = DateTime.UtcNow.AddSeconds(-timeoutSeconds);
        var batchSize = Math.Max(1, _options.Value.MaxBatchSize);

        var expiredCallIds = await db.CallSessions
            .AsNoTracking()
            .Where(c => c.Status == CallStatus.Ringing && c.RingingAtUtc != null && c.RingingAtUtc.Value <= cutoff)
            .OrderBy(c => c.RingingAtUtc)
            .Take(batchSize)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (expiredCallIds.Count == 0)
        {
            return 0;
        }

        _logger.LogInformation("Found {Count} expired ringing calls exceeding {TimeoutSeconds}s timeout. Processing transitions...",
            expiredCallIds.Count, timeoutSeconds);

        var processedCount = 0;

        foreach (var callId in expiredCallIds)
        {
            try
            {
                var result = await sender.Send(new MarkCallMissedCommand(callId), cancellationToken);
                if (result.IsSuccess)
                {
                    processedCount++;
                    _logger.LogInformation("Call {CallId} transitioned to Missed status due to ring timeout.", callId);
                }
                else
                {
                    _logger.LogDebug("Call {CallId} missed transition skipped: {Error}", callId, result.Error.Description);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to transition expired call {CallId} to Missed status.", callId);
            }
        }

        return processedCount;
    }
}
