using MealMate.Domain.VoiceCalls;

namespace MealMate.Application.Features.VoiceCalls.Dtos;

public sealed record RtcIceServerDto(
    IReadOnlyList<string> Urls,
    string? Username = null,
    string? Credential = null);

public sealed record TurnCredentialsResponseDto(
    string Username,
    string Password,
    int TtlSeconds,
    DateTime ExpiresAtUtc,
    IReadOnlyList<RtcIceServerDto> IceServers);

/// <summary>
/// Detailed representation of a VoIP call session.
/// </summary>
public sealed record CallSessionDto(
    Guid Id,
    Guid TripStopId,
    Guid CallerUserId,
    string CallerType,
    Guid ReceiverUserId,
    string ReceiverType,
    string Status,
    string? EndReason,
    DateTime StartedAtUtc,
    DateTime? RingingAtUtc,
    DateTime? AcceptedAtUtc,
    DateTime? ConnectedAtUtc,
    DateTime? EndedAtUtc,
    int? DurationSeconds,
    Guid ClientRequestId)
{
    public static CallSessionDto FromEntity(CallSession session) =>
        new(
            session.Id,
            session.TripStopId,
            session.CallerUserId,
            session.CallerType.ToString(),
            session.ReceiverUserId,
            session.ReceiverType.ToString(),
            session.Status.ToString(),
            session.EndReason?.ToString(),
            session.StartedAtUtc,
            session.RingingAtUtc,
            session.AcceptedAtUtc,
            session.ConnectedAtUtc,
            session.EndedAtUtc,
            session.DurationSeconds,
            session.ClientRequestId);
}

/// <summary>
/// Response returned upon successful initiation of a voice call session.
/// </summary>
public sealed record InitiateCallResponseDto(
    Guid CallId,
    Guid TripStopId,
    string Status,
    Guid CallerUserId,
    string CallerType,
    Guid ReceiverUserId,
    string ReceiverType,
    DateTime StartedAtUtc,
    DateTime? RingingAtUtc,
    Guid ClientRequestId)
{
    public static InitiateCallResponseDto FromEntity(CallSession session) =>
        new(
            session.Id,
            session.TripStopId,
            session.Status.ToString(),
            session.CallerUserId,
            session.CallerType.ToString(),
            session.ReceiverUserId,
            session.ReceiverType.ToString(),
            session.StartedAtUtc,
            session.RingingAtUtc,
            session.ClientRequestId);
}

/// <summary>
/// WebRTC Session Description Protocol (SDP) payload for offer/answer exchanges.
/// </summary>
public sealed record RtcSessionDescriptionDto(
    string Type,
    string Sdp);

/// <summary>
/// WebRTC Interactive Connectivity Establishment (ICE) candidate payload.
/// </summary>
public sealed record RtcIceCandidateDto(
    string Candidate,
    string? SdpMid = null,
    int? SdpMLineIndex = null,
    string? UsernameFragment = null);

/// <summary>
/// Standard envelope for relayed WebRTC signaling messages.
/// </summary>
public sealed record RtcSignalingEnvelopeDto<T>(
    Guid CallId,
    Guid SenderUserId,
    T Payload,
    DateTime SentAtUtc);

