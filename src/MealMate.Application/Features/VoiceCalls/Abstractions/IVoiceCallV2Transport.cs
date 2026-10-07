using MealMate.Application.Features.VoiceCalls.V2.Dtos;
using MealMate.Domain.VoiceCalls;

namespace MealMate.Application.Features.VoiceCalls.Abstractions;

public enum VoiceCallPushOutcome { Accepted, NoRoute, InvalidRoute, Suppressed }
public sealed record VoiceCallPushResult(VoiceCallPushOutcome Outcome, string? ProviderMessageId = null);

public interface IVoiceCallV2PushTransport
{
    Task<VoiceCallPushResult> SendAsync(VoiceCallDeviceSession device,
        CallEventEnvelope<CallSnapshotDto> envelope, CancellationToken cancellationToken);
}

public interface IVoiceCallV2RealtimeTransport
{
    Task<bool> SendAsync(Guid deviceSessionId, CallEventEnvelope<CallSnapshotDto> envelope,
        CancellationToken cancellationToken);
}
