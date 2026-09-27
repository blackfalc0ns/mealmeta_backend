namespace MealMate.Application.Features.VoiceCalls.Constants;

/// <summary>
/// Centralized immutable constants for all WebRTC and Voice Call SignalR event names.
/// Used by backend hubs, notifiers, test suites, and the mobile client application.
/// </summary>
public static class VoiceCallEvents
{
    // Hub connection handshake
    public const string ConnectionEstablished = "connection-established";

    // Server-to-Client lifecycle events
    public const string IncomingCall = "voice-call:incoming";
    public const string CallRinging = "voice-call:ringing";
    public const string CallAnswered = "voice-call:answered";
    public const string CallRejected = "voice-call:rejected";
    public const string CallCancelled = "voice-call:cancelled";
    public const string CallConnecting = "voice-call:connecting";
    public const string CallConnected = "voice-call:connected";
    public const string CallEnded = "voice-call:ended";
    public const string CallMissed = "voice-call:missed";
    public const string CallFailed = "voice-call:failed";

    // WebRTC signaling relay events
    public const string RtcOffer = "rtc:offer";
    public const string RtcAnswer = "rtc:answer";
    public const string RtcIceCandidate = "rtc:ice-candidate";
    public const string PeerMuteStateChanged = "peer-mute-state-changed";
}
