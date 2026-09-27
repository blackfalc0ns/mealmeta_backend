using MealMate.Domain.VoiceCalls;

namespace MealMate.Application.Features.VoiceCalls.Dtos;

/// <summary>
/// Strongly-typed failure categories reported by mobile WebRTC clients when peer connection setup fails.
/// Mapped safely to server-authoritative <see cref="CallEndReason"/> without storing arbitrary client exception strings.
/// </summary>
public enum ClientConnectionFailureReason
{
    Unknown = 0,
    IceFailed = 1,
    PeerConnectionFailed = 2,
    MediaUnavailable = 3,
    SignalingTimeout = 4,
    PermissionDenied = 5
}

public static class ClientConnectionFailureReasonExtensions
{
    public static CallEndReason ToCallEndReason(this ClientConnectionFailureReason clientReason)
    {
        return clientReason switch
        {
            ClientConnectionFailureReason.IceFailed => CallEndReason.ConnectionFailed,
            ClientConnectionFailureReason.PeerConnectionFailed => CallEndReason.ConnectionFailed,
            ClientConnectionFailureReason.SignalingTimeout => CallEndReason.Timeout,
            ClientConnectionFailureReason.MediaUnavailable => CallEndReason.ConnectionFailed,
            ClientConnectionFailureReason.PermissionDenied => CallEndReason.ConnectionFailed,
            _ => CallEndReason.ConnectionFailed
        };
    }

    public static ClientConnectionFailureReason FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ClientConnectionFailureReason.Unknown;

        return Enum.TryParse<ClientConnectionFailureReason>(value, ignoreCase: true, out var reason)
            ? reason
            : ClientConnectionFailureReason.Unknown;
    }
}
