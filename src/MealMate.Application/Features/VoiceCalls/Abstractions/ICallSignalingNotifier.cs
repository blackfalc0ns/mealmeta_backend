namespace MealMate.Application.Features.VoiceCalls.Abstractions;

/// <summary>
/// Abstraction for dispatching real-time voice call lifecycle events over WebSockets/SignalR.
/// </summary>
public interface ICallSignalingNotifier
{
    Task NotifyIncomingCallAsync(
        Guid callId,
        Guid receiverUserId,
        Guid callerUserId,
        string callerType,
        Guid? tripStopId,
        DateTime startedAtUtc,
        DateTime? ringingAtUtc,
        CancellationToken cancellationToken = default);

    Task NotifyCallRingingAsync(
        Guid callId,
        Guid callerUserId,
        DateTime ringingAtUtc,
        CancellationToken cancellationToken = default);

    Task NotifyCallAnsweredAsync(
        Guid callId,
        Guid callerUserId,
        DateTime answeredAtUtc,
        CancellationToken cancellationToken = default);

    Task NotifyCallAnsweredAsync(
        Guid callId,
        Guid callerUserId,
        DateTime answeredAtUtc,
        Guid? receiverUserId,
        CancellationToken cancellationToken = default);

    Task NotifyCallRejectedAsync(
        Guid callId,
        Guid callerUserId,
        string? reason,
        DateTime rejectedAtUtc,
        CancellationToken cancellationToken = default);

    Task NotifyCallCancelledAsync(
        Guid callId,
        Guid receiverUserId,
        string? reason,
        DateTime cancelledAtUtc,
        CancellationToken cancellationToken = default);

    Task NotifyCallConnectingAsync(
        Guid callId,
        Guid callerUserId,
        Guid receiverUserId,
        DateTime timestampUtc,
        CancellationToken cancellationToken = default);

    Task NotifyCallConnectedAsync(
        Guid callId,
        Guid callerUserId,
        Guid receiverUserId,
        DateTime connectedAtUtc,
        CancellationToken cancellationToken = default);

    Task NotifyCallEndedAsync(
        Guid callId,
        Guid peerUserId,
        string? reason,
        int? durationSeconds,
        DateTime endedAtUtc,
        CancellationToken cancellationToken = default);

    Task NotifyCallMissedAsync(
        Guid callId,
        Guid callerUserId,
        Guid receiverUserId,
        DateTime missedAtUtc,
        CancellationToken cancellationToken = default);

    Task NotifyCallFailedAsync(
        Guid callId,
        Guid callerUserId,
        Guid receiverUserId,
        string? reason,
        DateTime failedAtUtc,
        CancellationToken cancellationToken = default);
}
