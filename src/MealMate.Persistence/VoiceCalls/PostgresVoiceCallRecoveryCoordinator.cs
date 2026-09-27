using MealMate.Application.Features.VoiceCalls.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Persistence.VoiceCalls;

/// <summary>
/// PostgreSQL implementation of multi-instance background worker coordination using transaction-scoped
/// non-blocking advisory locks (pg_try_advisory_xact_lock).
/// Prevents multiple API instances from executing redundant recovery batches concurrently.
/// </summary>
public sealed class PostgresVoiceCallRecoveryCoordinator : IVoiceCallRecoveryCoordinator
{
    private readonly AppDbContext _db;
    private readonly ILogger<PostgresVoiceCallRecoveryCoordinator> _logger;

    public PostgresVoiceCallRecoveryCoordinator(
        AppDbContext db,
        ILogger<PostgresVoiceCallRecoveryCoordinator> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> TryExecuteRecoveryCycleAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsNpgsql())
        {
            await action(cancellationToken);
            return true;
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var lockAcquired = await _db.Database
                    .SqlQueryRaw<bool>(@"SELECT pg_try_advisory_xact_lock(hashtextextended('voice_call_recovery_worker', 0)) AS ""Value""")
                    .FirstOrDefaultAsync(cancellationToken);

                if (!lockAcquired)
                {
                    _logger.LogDebug("Another API instance is currently executing the voice call recovery worker. Skipping cycle.");
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }

                await action(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed or cancelled while executing coordinated recovery cycle.");
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                catch
                {
                    // Ignore secondary rollback failures
                }
                throw;
            }
        });
    }
}
