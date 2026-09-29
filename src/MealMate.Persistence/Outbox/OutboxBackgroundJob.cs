using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MealMate.Persistence.Outbox;

public sealed class OutboxBackgroundJob : BackgroundService, IOutboxService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxBackgroundJob>? _logger;
    private readonly IDateTimeProvider? _clock;
    private readonly OutboxOptions _options;
    private readonly string _workerId;

    public OutboxBackgroundJob(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxBackgroundJob>? logger = null,
        IDateTimeProvider? clock = null,
        IOptions<OutboxOptions>? options = null,
        string? workerId = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _clock = clock;
        _options = options?.Value ?? new OutboxOptions();
        _workerId = string.IsNullOrWhiteSpace(workerId) ? $"worker-{Guid.NewGuid():N}" : workerId;
    }

    public string WorkerId => _workerId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessagesAsync(_options.BatchSize, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger?.LogError(ex, "Unexpected error in OutboxBackgroundJob [{WorkerId}].", _workerId);
            }

            try
            {
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task<int> ProcessPendingMessagesAsync(CancellationToken cancellationToken = default)
        => await ProcessPendingMessagesAsync(_options.BatchSize, cancellationToken);

    public async Task<int> ProcessPendingMessagesAsync(int batchSize = 50, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var appDb = scope.ServiceProvider.GetService<AppDbContext>()
                    ?? (AppDbContext)scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var clock = _clock ?? scope.ServiceProvider.GetService<IDateTimeProvider>() ?? new SystemClockFallback();

        var nowUtc = clock.UtcNow;
        var leaseDuration = _options.LeaseDuration;
        var heartbeatInterval = _options.HeartbeatInterval;
        var isNpgsql = appDb.Database.IsNpgsql();

        var claimedIds = new List<Guid>();
        var currentLeaseToken = Guid.NewGuid();

        if (isNpgsql)
        {
            var expiresAtUtc = nowUtc.Add(leaseDuration);
            var sql = @"
                WITH candidate AS (
                    SELECT ""Id""
                    FROM ""OutboxMessages""
                    WHERE ""ProcessedAtUtc"" IS NULL
                      AND ""PoisonedAtUtc"" IS NULL
                      AND (""LockExpiresAtUtc"" IS NULL OR ""LockExpiresAtUtc"" < @nowUtc)
                      AND (""NextAttemptAtUtc"" IS NULL OR ""NextAttemptAtUtc"" <= @nowUtc)
                    ORDER BY ""CreatedAtUtc"" ASC
                    LIMIT @batchSize
                    FOR UPDATE SKIP LOCKED
                )
                UPDATE ""OutboxMessages"" m
                SET ""LockedByWorkerId"" = @workerId,
                    ""LockExpiresAtUtc"" = @expiresAtUtc,
                    ""LeaseToken"" = @leaseToken,
                    ""LastAttemptedAtUtc"" = @nowUtc
                FROM candidate
                WHERE m.""Id"" = candidate.""Id""
                RETURNING m.""Id"";";

            var conn = appDb.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync(cancellationToken);
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            var p1 = cmd.CreateParameter();
            p1.ParameterName = "nowUtc";
            p1.Value = nowUtc;
            cmd.Parameters.Add(p1);

            var p2 = cmd.CreateParameter();
            p2.ParameterName = "batchSize";
            p2.Value = batchSize;
            cmd.Parameters.Add(p2);

            var p3 = cmd.CreateParameter();
            p3.ParameterName = "workerId";
            p3.Value = _workerId;
            cmd.Parameters.Add(p3);

            var p4 = cmd.CreateParameter();
            p4.ParameterName = "expiresAtUtc";
            p4.Value = expiresAtUtc;
            cmd.Parameters.Add(p4);

            var p5 = cmd.CreateParameter();
            p5.ParameterName = "leaseToken";
            p5.Value = currentLeaseToken;
            cmd.Parameters.Add(p5);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                claimedIds.Add(reader.GetGuid(0));
            }
        }
        else
        {
            var candidate = await appDb.OutboxMessages
                .Where(m => m.ProcessedAtUtc == null &&
                            m.PoisonedAtUtc == null &&
                            (m.LockExpiresAtUtc == null || m.LockExpiresAtUtc < nowUtc) &&
                            (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= nowUtc))
                .OrderBy(m => m.CreatedAtUtc)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            foreach (var m in candidate)
            {
                m.LockedByWorkerId = _workerId;
                m.LockExpiresAtUtc = nowUtc.Add(leaseDuration);
                m.LeaseToken = currentLeaseToken;
                m.LastAttemptedAtUtc = nowUtc;
                claimedIds.Add(m.Id);
            }
            if (candidate.Count > 0)
            {
                await appDb.SaveChangesAsync(cancellationToken);
            }
        }

        if (claimedIds.Count == 0) return 0;

        var processedCount = 0;

        foreach (var id in claimedIds)
        {
            var msg = await appDb.OutboxMessages.FindAsync(new object[] { id }, cancellationToken);
            if (msg == null) continue;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Background heartbeat timer
            var heartbeatTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(heartbeatInterval, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (cts.Token.IsCancellationRequested) break;

                    try
                    {
                        using var hbScope = _scopeFactory.CreateScope();
                        var hbDb = hbScope.ServiceProvider.GetService<AppDbContext>()
                                   ?? (AppDbContext)hbScope.ServiceProvider.GetRequiredService<IAppDbContext>();
                        var hbClock = _clock ?? hbScope.ServiceProvider.GetService<IDateTimeProvider>() ?? new SystemClockFallback();
                        var newExpires = hbClock.UtcNow.Add(leaseDuration);

                        int rowsUpdated;
                        if (isNpgsql)
                        {
                            rowsUpdated = await hbDb.Database.ExecuteSqlInterpolatedAsync($@"
                                UPDATE ""OutboxMessages""
                                SET ""LockExpiresAtUtc"" = {newExpires}
                                WHERE ""Id"" = {msg.Id}
                                  AND ""LockedByWorkerId"" = {_workerId}
                                  AND ""LeaseToken"" = {currentLeaseToken}
                                  AND ""ProcessedAtUtc"" IS NULL;
                            ", CancellationToken.None);
                        }
                        else
                        {
                            var target = await hbDb.OutboxMessages.FindAsync(new object[] { msg.Id });
                            if (target != null && target.LockedByWorkerId == _workerId && target.LeaseToken == currentLeaseToken && target.ProcessedAtUtc == null)
                            {
                                target.LockExpiresAtUtc = newExpires;
                                rowsUpdated = await hbDb.SaveChangesAsync();
                            }
                            else
                            {
                                rowsUpdated = 0;
                            }
                        }

                        if (rowsUpdated == 0)
                        {
                            _logger?.LogWarning("Outbox message {Id} lease renewal failed; ownership lost to another worker.", msg.Id);
                            cts.Cancel();
                            break;
                        }
                    }
                    catch (Exception ex) when (!cts.Token.IsCancellationRequested)
                    {
                        _logger?.LogWarning(ex, "Heartbeat renewal error for outbox message {Id}.", msg.Id);
                    }
                }
            });

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

                await mediator.Publish(notification, cts.Token);

                cts.Cancel();
                await Task.WhenAny(heartbeatTask, Task.Delay(50));

                var completeNow = clock.UtcNow;
                int rows;

                if (isNpgsql)
                {
                    rows = await appDb.Database.ExecuteSqlInterpolatedAsync($@"
                        UPDATE ""OutboxMessages""
                        SET ""ProcessedAtUtc"" = {completeNow},
                            ""LockedByWorkerId"" = NULL,
                            ""LockExpiresAtUtc"" = NULL,
                            ""LeaseToken"" = NULL,
                            ""LastError"" = NULL
                        WHERE ""Id"" = {msg.Id}
                          AND ""LockedByWorkerId"" = {_workerId}
                          AND ""LeaseToken"" = {currentLeaseToken}
                          AND ""ProcessedAtUtc"" IS NULL;
                    ", cancellationToken);
                }
                else
                {
                    var target = await appDb.OutboxMessages.FindAsync(new object[] { msg.Id }, cancellationToken);
                    if (target != null && target.LockedByWorkerId == _workerId && target.LeaseToken == currentLeaseToken && target.ProcessedAtUtc == null)
                    {
                        target.ProcessedAtUtc = completeNow;
                        target.LockedByWorkerId = null;
                        target.LockExpiresAtUtc = null;
                        target.LeaseToken = null;
                        target.LastError = null;
                        rows = await appDb.SaveChangesAsync(cancellationToken);
                    }
                    else
                    {
                        rows = 0;
                    }
                }

                if (rows > 0)
                {
                    processedCount++;
                }
                else
                {
                    _logger?.LogWarning("Outbox message {Id} lease expired or was taken over before completion by worker {WorkerId}.", msg.Id, _workerId);
                }
            }
            catch (Exception ex)
            {
                cts.Cancel();
                await Task.WhenAny(heartbeatTask, Task.Delay(50));

                var retryCount = msg.RetryCount + 1;
                var delays = new[] { 1, 5, 15, 60, 300 };
                var delaySec = delays[Math.Min(retryCount - 1, delays.Length - 1)];
                var nextAttempt = retryCount >= 5 ? (DateTime?)null : clock.UtcNow.AddSeconds(delaySec);
                var poisonedAt = retryCount >= 5 ? (DateTime?)clock.UtcNow : null;

                if (isNpgsql)
                {
                    await appDb.Database.ExecuteSqlInterpolatedAsync($@"
                        UPDATE ""OutboxMessages""
                        SET ""RetryCount"" = {retryCount},
                            ""LastError"" = {ex.Message},
                            ""LockedByWorkerId"" = NULL,
                            ""LockExpiresAtUtc"" = NULL,
                            ""LeaseToken"" = NULL,
                            ""NextAttemptAtUtc"" = {nextAttempt},
                            ""PoisonedAtUtc"" = {poisonedAt}
                        WHERE ""Id"" = {msg.Id}
                          AND ""LockedByWorkerId"" = {_workerId}
                          AND ""LeaseToken"" = {currentLeaseToken}
                          AND ""ProcessedAtUtc"" IS NULL;
                    ", cancellationToken);
                }
                else
                {
                    var target = await appDb.OutboxMessages.FindAsync(new object[] { msg.Id }, cancellationToken);
                    if (target != null && target.LockedByWorkerId == _workerId && target.LeaseToken == currentLeaseToken && target.ProcessedAtUtc == null)
                    {
                        target.RetryCount = retryCount;
                        target.LastError = ex.Message;
                        target.LockedByWorkerId = null;
                        target.LockExpiresAtUtc = null;
                        target.LeaseToken = null;
                        target.NextAttemptAtUtc = nextAttempt;
                        target.PoisonedAtUtc = poisonedAt;
                        await appDb.SaveChangesAsync(cancellationToken);
                    }
                }

                if (retryCount >= 5)
                {
                    _logger?.LogError(ex, "Outbox message {Id} poisoned after {Retries} attempts.", msg.Id, retryCount);
                }
                else
                {
                    _logger?.LogWarning(ex, "Outbox message {Id} failed attempt {Retries}. Next attempt in {Delay}s.", msg.Id, retryCount, delaySec);
                }
            }
        }

        return processedCount;
    }

    private sealed class SystemClockFallback : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;

        public DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);
    }
}
