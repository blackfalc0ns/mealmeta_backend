using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.VoiceCalls.Abstractions;
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
/// Unified multi-instance background worker that scans and recovers stale VoIP call sessions:
/// 1. Ringing calls exceeding RingTimeoutSeconds -> Missed (NoAnswer).
/// 2. Accepted calls exceeding AcceptedSetupTimeoutSeconds -> Failed (ConnectionFailed).
/// 3. Connecting calls exceeding ConnectingTimeoutSeconds -> Failed (ConnectionFailed).
/// 4. Active calls exceeding MaximumCallDurationMinutes -> Ended (Timeout).
/// Protected across horizontally-scaled API instances using <see cref="IVoiceCallRecoveryCoordinator"/>.
/// </summary>
public sealed class VoiceCallRecoveryJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<VoiceCallOptions> _options;
    private readonly IVoiceCallRecoveryCoordinator? _explicitCoordinator;
    private readonly ILogger<VoiceCallRecoveryJob> _logger;

    public VoiceCallRecoveryJob(
        IServiceScopeFactory scopeFactory,
        IOptions<VoiceCallOptions> options,
        ILogger<VoiceCallRecoveryJob> logger)
        : this(scopeFactory, options, null, logger)
    {
    }

    internal VoiceCallRecoveryJob(
        IServiceScopeFactory scopeFactory,
        IOptions<VoiceCallOptions> options,
        IVoiceCallRecoveryCoordinator? coordinator,
        ILogger<VoiceCallRecoveryJob> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _explicitCoordinator = coordinator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = Math.Max(1, _options.Value.TimeoutWorkerIntervalSeconds);
        var interval = TimeSpan.FromSeconds(intervalSeconds);

        _logger.LogInformation("VoiceCallRecoveryJob started with interval {Interval}s. RingTimeout={Ring}s, AcceptedTimeout={Acc}s, ConnectingTimeout={Conn}s, MaxDuration={Max}m.",
            intervalSeconds,
            _options.Value.RingTimeoutSeconds,
            _options.Value.AcceptedSetupTimeoutSeconds,
            _options.Value.ConnectingTimeoutSeconds,
            _options.Value.MaximumCallDurationMinutes);

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var coordinator = _explicitCoordinator
                    ?? scope.ServiceProvider.GetService<IVoiceCallRecoveryCoordinator>()
                    ?? NullVoiceCallRecoveryCoordinator.Instance;

                await coordinator.TryExecuteRecoveryCycleAsync(
                    async ct => await ProcessAllExpiredCallsAsync(ct),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in VoiceCallRecoveryJob cycle.");
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

    public async Task<int> ProcessAllExpiredCallsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var total = 0;
        total += await ProcessExpiredRingingCallsAsync(db, sender, cancellationToken);
        total += await ProcessExpiredAcceptedCallsAsync(db, sender, cancellationToken);
        total += await ProcessExpiredConnectingCallsAsync(db, sender, cancellationToken);
        total += await ProcessAbandonedActiveCallsAsync(db, sender, cancellationToken);

        return total;
    }

    public async Task<int> ProcessExpiredRingingCallsAsync(IAppDbContext db, ISender sender, CancellationToken cancellationToken)
    {
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

        var processed = 0;
        foreach (var callId in expiredCallIds)
        {
            try
            {
                var result = await sender.Send(new MarkCallMissedCommand(callId), cancellationToken);
                if (result.IsSuccess)
                {
                    processed++;
                    _logger.LogInformation("Call {CallId} recovered: Ringing -> Missed (Timeout).", callId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to recover expired ringing call {CallId}.", callId);
            }
        }

        return processed;
    }

    public async Task<int> ProcessExpiredAcceptedCallsAsync(IAppDbContext db, ISender sender, CancellationToken cancellationToken)
    {
        var timeoutSeconds = Math.Max(5, _options.Value.AcceptedSetupTimeoutSeconds);
        var cutoff = DateTime.UtcNow.AddSeconds(-timeoutSeconds);
        var batchSize = Math.Max(1, _options.Value.MaxBatchSize);

        var expiredCallIds = await db.CallSessions
            .AsNoTracking()
            .Where(c => c.Status == CallStatus.Accepted && c.AcceptedAtUtc != null && c.AcceptedAtUtc.Value <= cutoff)
            .OrderBy(c => c.AcceptedAtUtc)
            .Take(batchSize)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var callId in expiredCallIds)
        {
            try
            {
                var result = await sender.Send(new MarkCallFailedCommand(callId, CallEndReason.ConnectionFailed, IsSystem: true), cancellationToken);
                if (result.IsSuccess)
                {
                    processed++;
                    _logger.LogInformation("Call {CallId} recovered: Accepted -> Failed (Setup Timeout).", callId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to recover expired accepted call {CallId}.", callId);
            }
        }

        return processed;
    }

    public async Task<int> ProcessExpiredConnectingCallsAsync(IAppDbContext db, ISender sender, CancellationToken cancellationToken)
    {
        var timeoutSeconds = Math.Max(5, _options.Value.ConnectingTimeoutSeconds);
        var cutoff = DateTime.UtcNow.AddSeconds(-timeoutSeconds);
        var batchSize = Math.Max(1, _options.Value.MaxBatchSize);

        var expiredCallIds = await db.CallSessions
            .AsNoTracking()
            .Where(c => c.Status == CallStatus.Connecting &&
                        ((c.ConnectingAtUtc != null && c.ConnectingAtUtc.Value <= cutoff) ||
                         (c.ConnectingAtUtc == null && c.AcceptedAtUtc != null && c.AcceptedAtUtc.Value <= cutoff)))
            .OrderBy(c => c.ConnectingAtUtc ?? c.AcceptedAtUtc)
            .Take(batchSize)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var callId in expiredCallIds)
        {
            try
            {
                var result = await sender.Send(new MarkCallFailedCommand(callId, CallEndReason.ConnectionFailed, IsSystem: true), cancellationToken);
                if (result.IsSuccess)
                {
                    processed++;
                    _logger.LogInformation("Call {CallId} recovered: Connecting -> Failed (Media Negotiation Timeout).", callId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to recover expired connecting call {CallId}.", callId);
            }
        }

        return processed;
    }

    public async Task<int> ProcessAbandonedActiveCallsAsync(IAppDbContext db, ISender sender, CancellationToken cancellationToken)
    {
        var maxDurationMinutes = Math.Max(10, _options.Value.MaximumCallDurationMinutes);
        var cutoff = DateTime.UtcNow.AddMinutes(-maxDurationMinutes);
        var batchSize = Math.Max(1, _options.Value.MaxBatchSize);

        var expiredCallIds = await db.CallSessions
            .AsNoTracking()
            .Where(c => c.Status == CallStatus.Active && c.ConnectedAtUtc != null && c.ConnectedAtUtc.Value <= cutoff)
            .OrderBy(c => c.ConnectedAtUtc)
            .Take(batchSize)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var callId in expiredCallIds)
        {
            try
            {
                var result = await sender.Send(new EndCallCommand(callId, CallEndReason.Timeout.ToString(), IsSystem: true), cancellationToken);
                if (result.IsSuccess)
                {
                    processed++;
                    _logger.LogInformation("Call {CallId} recovered: Active -> Ended (Maximum Duration Exceeded).", callId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to recover abandoned active call {CallId}.", callId);
            }
        }

        return processed;
    }
}
