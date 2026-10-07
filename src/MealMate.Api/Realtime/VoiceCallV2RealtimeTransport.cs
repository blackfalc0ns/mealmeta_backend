using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Options;
using MealMate.Application.Features.VoiceCalls.V2.Dtos;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace MealMate.Api.Realtime;

public sealed class VoiceCallV2RealtimeTransport(
    IAppDbContext db,
    IHubContext<VoiceCallV2Hub> hub,
    VoiceCallV2ConnectionRegistry connections,
    IDateTimeProvider clock,
    IOptions<VoiceCallOptions> options) : IVoiceCallV2RealtimeTransport
{
    public Task<bool> SendAsync(Guid deviceSessionId, CallEventEnvelope<CallSnapshotDto> envelope,
        CancellationToken cancellationToken)
    {
        if (!options.Value.V2Enabled) return Task.FromResult(false);
        return connections.SendAsync(db, clock, deviceSessionId,
            group => hub.Clients.Group(group).SendAsync(
                VoiceCallV2Hub.EventCall, envelope, cancellationToken), cancellationToken);
    }
}
