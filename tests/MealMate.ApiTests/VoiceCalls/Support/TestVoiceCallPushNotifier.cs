using System.Collections.Concurrent;
using MealMate.Application.Features.VoiceCalls.Abstractions;

namespace MealMate.ApiTests.VoiceCalls.Support;

/// <summary>
/// Fault-injectable push transport sink for the isolated voice-call test host.
/// Records dispatch attempts; never touches a real provider.
/// </summary>
public sealed class TestVoiceCallPushNotifier : IVoiceCallPushNotifier, IVoiceCallV2PushTransport
{
    public ConcurrentBag<(Guid CallId, Guid ReceiverUserId, Guid CallerUserId, string CallerType, Guid? TripStopId)> Incoming { get; } = new();
    public ConcurrentBag<(Guid CallId, Guid ReceiverUserId)> Cancelled { get; } = new();
    public ConcurrentBag<(Guid CallId, Guid ReceiverUserId)> Missed { get; } = new();

    public Func<Task>? OnIncomingCallAsync { get; set; }
    public Func<Task>? OnCancelledAsync { get; set; }
    public Func<Task>? OnMissedAsync { get; set; }

    public ConcurrentBag<(Guid DeviceId, MealMate.Application.Features.VoiceCalls.V2.Dtos.CallEventEnvelope<MealMate.Application.Features.VoiceCalls.V2.Dtos.CallSnapshotDto> Envelope)> V2Events { get; } = new();

    public async Task<VoiceCallPushResult> SendAsync(MealMate.Domain.VoiceCalls.VoiceCallDeviceSession device,
        MealMate.Application.Features.VoiceCalls.V2.Dtos.CallEventEnvelope<MealMate.Application.Features.VoiceCalls.V2.Dtos.CallSnapshotDto> envelope,
        CancellationToken cancellationToken)
    {
        switch (envelope.EventType)
        {
            case "incoming":
                await SendIncomingCallPushAsync(envelope.CallId, device.UserId, Guid.Empty,
                    envelope.Payload.LocalParticipantRole, envelope.Payload.TripStopId, cancellationToken);
                break;
            case "cancelled": await SendCallCancelledPushAsync(envelope.CallId, device.UserId, cancellationToken); break;
            case "missed": await SendCallMissedPushAsync(envelope.CallId, device.UserId, cancellationToken); break;
        }
        V2Events.Add((device.Id, envelope));
        return new(VoiceCallPushOutcome.Accepted, "isolated-test-provider");
    }

    public async Task SendIncomingCallPushAsync(
        Guid callId, Guid receiverUserId, Guid callerUserId, string callerType, Guid? tripStopId,
        CancellationToken cancellationToken = default)
    {
        if (OnIncomingCallAsync is not null)
        {
            await OnIncomingCallAsync();
        }

        Incoming.Add((callId, receiverUserId, callerUserId, callerType, tripStopId));
    }

    public async Task SendCallCancelledPushAsync(Guid callId, Guid receiverUserId, CancellationToken cancellationToken = default)
    {
        if (OnCancelledAsync is not null)
        {
            await OnCancelledAsync();
        }

        Cancelled.Add((callId, receiverUserId));
    }

    public async Task SendCallMissedPushAsync(Guid callId, Guid receiverUserId, CancellationToken cancellationToken = default)
    {
        if (OnMissedAsync is not null)
        {
            await OnMissedAsync();
        }

        Missed.Add((callId, receiverUserId));
    }
}
