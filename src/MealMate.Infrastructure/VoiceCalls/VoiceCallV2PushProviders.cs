using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.V2.Dtos;

namespace MealMate.Infrastructure.VoiceCalls;

public interface IVoiceCallV2FcmProvider
{
    Task<VoiceCallPushResult> SendAsync(string token, CallEventEnvelope<CallSnapshotDto> envelope,
        TimeSpan ttl, CancellationToken cancellationToken);
}

public interface IVoiceCallV2ApnsProvider
{
    Task<VoiceCallPushResult> SendAsync(string token, CallEventEnvelope<CallSnapshotDto> envelope,
        CancellationToken cancellationToken);
}
