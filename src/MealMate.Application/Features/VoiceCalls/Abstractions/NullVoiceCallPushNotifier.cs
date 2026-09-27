namespace MealMate.Application.Features.VoiceCalls.Abstractions;

/// <summary>
/// Null-object implementation of <see cref="IVoiceCallPushNotifier"/> for unit tests and decoupled runs.
/// </summary>
public sealed class NullVoiceCallPushNotifier : IVoiceCallPushNotifier
{
    public static readonly NullVoiceCallPushNotifier Instance = new();

    private NullVoiceCallPushNotifier() { }

    public Task SendIncomingCallPushAsync(
        Guid callId,
        Guid receiverUserId,
        Guid callerUserId,
        string callerType,
        Guid? tripStopId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendCallCancelledPushAsync(
        Guid callId,
        Guid receiverUserId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendCallMissedPushAsync(
        Guid callId,
        Guid receiverUserId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
