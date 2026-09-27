namespace MealMate.Application.Features.VoiceCalls.Abstractions;

/// <summary>
/// Abstraction for dispatching high-priority data-only push notifications for VoIP call wake-up and teardown.
/// </summary>
public interface IVoiceCallPushNotifier
{
    Task SendIncomingCallPushAsync(
        Guid callId,
        Guid receiverUserId,
        Guid callerUserId,
        string callerType,
        Guid? tripStopId,
        CancellationToken cancellationToken = default);

    Task SendCallCancelledPushAsync(
        Guid callId,
        Guid receiverUserId,
        CancellationToken cancellationToken = default);

    Task SendCallMissedPushAsync(
        Guid callId,
        Guid receiverUserId,
        CancellationToken cancellationToken = default);
}
