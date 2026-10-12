using MealMate.Application.Features.VoiceCalls.V2.Dtos;
using MealMate.Domain.VoiceCalls;

namespace MealMate.Application.Features.VoiceCalls.Abstractions;

public enum VoiceCallPushOutcome { Accepted, NoRoute, InvalidRoute, Suppressed }
public sealed record VoiceCallPushResult(VoiceCallPushOutcome Outcome, string? ProviderMessageId = null, string? ReasonCode = null);

/// <summary>
/// Stable, non-localized diagnostics codes explaining why a v2 voice target was
/// not delivered. These are technical identifiers (never user-facing copy) and
/// deliberately exclude tokens, proofs, SDP/ICE data or provider payloads.
/// </summary>
public static class VoiceCallPushReasons
{
    public const string RealtimeOnly = "realtime_only";
    public const string SessionRevoked = "session_revoked";
    public const string SessionExpired = "session_expired";
    public const string UnsupportedPlatform = "unsupported_platform";
    public const string MissingDeadline = "missing_deadline";
    public const string IncomingExpired = "incoming_expired";
    public const string MissingVoipRoute = "missing_voip_route";
    public const string MissingFcmReference = "missing_fcm_reference";
    public const string MissingOwnedFcmRoute = "missing_owned_fcm_route";
    public const string InactiveFcmRoute = "inactive_fcm_route";
    public const string PlatformMismatch = "platform_mismatch";
    public const string EmptyFcmToken = "empty_fcm_token";
    public const string ProviderUnregistered = "provider_unregistered";
    public const string ObsoleteLifecycle = "obsolete_lifecycle";
    public const string HubUnbound = "hub_unbound";
    public const string TransportRejected = "transport_rejected";
}

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
