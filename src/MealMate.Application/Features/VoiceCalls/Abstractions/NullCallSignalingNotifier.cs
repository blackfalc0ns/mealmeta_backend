namespace MealMate.Application.Features.VoiceCalls.Abstractions;

/// <summary>
/// Null-object implementation of <see cref="ICallSignalingNotifier"/> used in unit tests
/// and decoupled workflows when no active realtime transport is configured.
/// </summary>
public sealed class NullCallSignalingNotifier : ICallSignalingNotifier
{
    public static readonly NullCallSignalingNotifier Instance = new();

    private NullCallSignalingNotifier() { }

    public Task NotifyIncomingCallAsync(
        Guid callId,
        Guid receiverUserId,
        Guid callerUserId,
        string callerType,
        Guid? tripStopId,
        DateTime startedAtUtc,
        DateTime? ringingAtUtc,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyCallRingingAsync(
        Guid callId,
        Guid callerUserId,
        DateTime ringingAtUtc,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyCallAnsweredAsync(
        Guid callId,
        Guid callerUserId,
        DateTime answeredAtUtc,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyCallAnsweredAsync(
        Guid callId,
        Guid callerUserId,
        DateTime answeredAtUtc,
        Guid? receiverUserId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyCallRejectedAsync(
        Guid callId,
        Guid callerUserId,
        string? reason,
        DateTime rejectedAtUtc,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyCallCancelledAsync(
        Guid callId,
        Guid receiverUserId,
        string? reason,
        DateTime cancelledAtUtc,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyCallConnectingAsync(
        Guid callId,
        Guid callerUserId,
        Guid receiverUserId,
        DateTime timestampUtc,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyCallConnectedAsync(
        Guid callId,
        Guid callerUserId,
        Guid receiverUserId,
        DateTime connectedAtUtc,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyCallEndedAsync(
        Guid callId,
        Guid peerUserId,
        string? reason,
        int? durationSeconds,
        DateTime endedAtUtc,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyCallMissedAsync(
        Guid callId,
        Guid callerUserId,
        Guid receiverUserId,
        DateTime missedAtUtc,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NotifyCallFailedAsync(
        Guid callId,
        Guid callerUserId,
        Guid receiverUserId,
        string? reason,
        DateTime failedAtUtc,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
