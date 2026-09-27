using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Persistence.VoiceCalls;

/// <summary>
/// PostgreSQL implementation of participant distributed concurrency locking using transaction-scoped
/// advisory locks (pg_advisory_xact_lock) derived from deterministic 64-bit hashes of user GUIDs.
/// Participants are locked in ascending GUID order to guarantee deadlock freedom (e.g. A calls B while B calls A).
/// Locks are automatically released when the database transaction commits or rolls back.
/// </summary>
public sealed class PostgresVoiceCallParticipantLockService : IVoiceCallParticipantLockService
{
    private readonly AppDbContext _db;
    private readonly ILogger<PostgresVoiceCallParticipantLockService> _logger;

    public PostgresVoiceCallParticipantLockService(
        AppDbContext db,
        ILogger<PostgresVoiceCallParticipantLockService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<T> ExecuteInParticipantLockAsync<T>(
        Guid participant1,
        Guid participant2,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsNpgsql())
        {
            return await action(cancellationToken);
        }

        // Sort participant GUIDs deterministically to prevent deadlocks (User A -> B vs User B -> A)
        var (first, second) = participant1.CompareTo(participant2) <= 0
            ? (participant1, participant2)
            : (participant2, participant1);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Acquire transaction-scoped advisory lock on first participant
            var key1 = $"voice_call_user:{first:N}";
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({key1}, 0))", cancellationToken);

            if (first != second)
            {
                // Acquire transaction-scoped advisory lock on second participant
                var key2 = $"voice_call_user:{second:N}";
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({key2}, 0))", cancellationToken);
            }

            var result = await action(cancellationToken);

            if (result is Result { IsFailure: true })
            {
                await transaction.RollbackAsync(cancellationToken);
                return result;
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Transaction or participant lock failed for participants {P1} and {P2}", participant1, participant2);
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
