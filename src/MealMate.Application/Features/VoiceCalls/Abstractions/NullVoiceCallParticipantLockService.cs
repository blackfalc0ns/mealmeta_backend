namespace MealMate.Application.Features.VoiceCalls.Abstractions;

/// <summary>
/// Null-object implementation of <see cref="IVoiceCallParticipantLockService"/> for unit tests and fallback.
/// </summary>
public sealed class NullVoiceCallParticipantLockService : IVoiceCallParticipantLockService
{
    public static readonly NullVoiceCallParticipantLockService Instance = new();

    private NullVoiceCallParticipantLockService() { }

    public Task<T> ExecuteInParticipantLockAsync<T>(
        Guid participant1,
        Guid participant2,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        return action(cancellationToken);
    }
}
