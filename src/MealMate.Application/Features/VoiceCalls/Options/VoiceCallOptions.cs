namespace MealMate.Application.Features.VoiceCalls.Options;

/// <summary>
/// Authoritative server configuration options for Voice Calls lifecycle, timeouts, recovery, and rate limits.
/// </summary>
public sealed class VoiceCallOptions
{
    public const string SectionName = "VoiceCalls";

    /// <summary>
    /// Duration in seconds a call may stay in Ringing status before the server transitions it to Missed (NoAnswer).
    /// Default: 30 seconds.
    /// </summary>
    public int RingTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Duration in seconds a call may stay in Accepted status waiting for client WebRTC setup to begin before timing out as Failed (ConnectionFailed).
    /// Default: 20 seconds.
    /// </summary>
    public int AcceptedSetupTimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// Duration in seconds a call may stay in Connecting status (ICE/DTLS media negotiation) before timing out as Failed (ConnectionFailed).
    /// Default: 30 seconds.
    /// </summary>
    public int ConnectingTimeoutSeconds { get; set; } = 30;

    /// <summary>Maximum silence from either active peer before network-loss recovery.</summary>
    public int MediaLeaseSeconds { get; set; } = 45;

    /// <summary>
    /// Maximum permitted continuous duration for an Active call in minutes before being automatically terminated as Ended (Timeout).
    /// Prevents zombie calls when both clients crash or lose power without issuing EndCall.
    /// Default: 240 minutes (4 hours).
    /// </summary>
    public int MaximumCallDurationMinutes { get; set; } = 240;

    /// <summary>
    /// Polling frequency in seconds for the background recovery worker.
    /// Default: 5 seconds.
    /// </summary>
    public int TimeoutWorkerIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Maximum batch size of expired calls to query and process per worker tick.
    /// Default: 50.
    /// </summary>
    public int MaxBatchSize { get; set; } = 50;

    /// <summary>
    /// Maximum allowed call initiation requests per minute per user to prevent harassment and spam.
    /// Default: 5 requests per minute.
    /// </summary>
    public int StartCallRateLimitPerMinute { get; set; } = 5;

    /// <summary>
    /// Protocol versioning (T05). V2 enables the device-session protocol under
    /// /api/v2/voice-calls and /hubs/voice-call-v2. The flag stays <c>false</c>
    /// until the v2 implementation tasks land and their gates pass - a disabled
    /// flag is reported as disabled, never faked as success.
    /// </summary>
    public bool V2Enabled { get; set; }

    /// <summary>
    /// Legacy (v1) call creation switch. Disabling it stops NEW v1 calls while the
    /// v1 read/terminal paths keep serving and draining existing sessions.
    /// </summary>
    public bool LegacyInitiationEnabled { get; set; } = true;

    /// <summary>
    /// Cooldown after a qualified no-answer attempt before the driver may resume
    /// (spec 4.2 proposed 120s; pending G-POLICY adoption).
    /// </summary>
    public int RetryCooldownSeconds { get; set; } = 120;
}
