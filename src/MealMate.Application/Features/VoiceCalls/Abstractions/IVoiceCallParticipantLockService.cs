namespace MealMate.Application.Features.VoiceCalls.Abstractions;

/// <summary>
/// Abstraction for acquiring distributed cross-request participant locks during call setup.
/// In PostgreSQL, this is backed by transaction-scoped advisory locks (pg_advisory_xact_lock)
/// ordered deterministically by user GUID to prevent deadlocks and race conditions.
/// </summary>
public interface IVoiceCallParticipantLockService
{
    Task<T> ExecuteInParticipantLockAsync<T>(
        Guid participant1,
        Guid participant2,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default);
}
