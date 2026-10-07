namespace MealMate.Application.Features.VoiceCalls.Abstractions;

/// <summary>
/// Abstraction for acquiring distributed cross-request participant locks during call setup.
/// In PostgreSQL, this is backed by transaction-scoped advisory locks (pg_advisory_xact_lock)
/// ordered deterministically by user GUID to prevent deadlocks and race conditions.
/// </summary>
public interface IVoiceCallParticipantLockService
{
    /// <summary>
    /// When saving fails with a unique-constraint violation,
    /// <paramref name="reconcileOnUniqueViolationAsync"/> may resolve the winner's state
    /// on the rolled-back (clean) context; returning null rethrows the original failure.
    /// </summary>
    Task<T> ExecuteInParticipantLockAsync<T>(
        Guid participant1,
        Guid participant2,
        Func<CancellationToken, Task<T>> action,
        Func<CancellationToken, Task<T?>>? reconcileOnUniqueViolationAsync = null,
        CancellationToken cancellationToken = default);
}
