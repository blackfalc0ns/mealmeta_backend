using MealMate.Domain.VoiceCalls;

namespace MealMate.Application.Features.VoiceCalls.V2.Dtos;

public static class VoiceCallSnapshots
{
    public static CallSnapshotDto ForParticipant(CallSession call, Guid userId)
    {
        if (userId != call.CallerUserId && userId != call.ReceiverUserId)
            throw new ArgumentException("Participant required.", nameof(userId));
        var role = userId == call.CallerUserId ? call.CallerType : call.ReceiverType;
        return new(call.Id, call.TripStopId, call.ProtocolVersion, call.Sequence,
            call.Status.ToString(), call.EndReason?.ToString(), call.StartedAtUtc,
            call.AcceptedAtUtc, call.ConnectedAtUtc, call.EndedAtUtc, call.DurationSeconds,
            call.RingExpiresAtUtc, call.ReceiverDeviceSessionId, role.ToString());
    }
}
