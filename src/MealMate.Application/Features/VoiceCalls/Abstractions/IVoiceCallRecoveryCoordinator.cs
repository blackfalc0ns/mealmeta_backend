namespace MealMate.Application.Features.VoiceCalls.Abstractions;

/// <summary>
/// Coordinates background voice call recovery sweeps across multiple horizontally-scaled API instances.
/// Ensures that at most one instance executes a recovery batch at any given moment.
/// </summary>
public interface IVoiceCallRecoveryCoordinator
{
    /// <summary>
    /// Attempts to execute the recovery action under an instance-level distributed lock.
    /// If another instance is currently running the recovery sweep, returns false immediately without blocking.
    /// </summary>
    Task<bool> TryExecuteRecoveryCycleAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Null-object implementation of <see cref="IVoiceCallRecoveryCoordinator"/> for unit tests and fallback.
/// </summary>
public sealed class NullVoiceCallRecoveryCoordinator : IVoiceCallRecoveryCoordinator
{
    public static readonly NullVoiceCallRecoveryCoordinator Instance = new();

    private NullVoiceCallRecoveryCoordinator() { }

    public async Task<bool> TryExecuteRecoveryCycleAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        await action(cancellationToken);
        return true;
    }
}
