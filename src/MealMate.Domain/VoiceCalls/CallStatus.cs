namespace MealMate.Domain.VoiceCalls;

/// <summary>
/// Lifecycle statuses for 1-to-1 in-app VoIP call sessions.
/// Distinguishes call establishment, WebRTC negotiation, live audio, and terminal states.
/// </summary>
public enum CallStatus
{
    Created = 1,
    Ringing = 2,
    Accepted = 3,
    Connecting = 4,
    Active = 5,

    Rejected = 10,
    Cancelled = 11,
    Missed = 12,
    Failed = 13,
    Ended = 14,
}
