using MealMate.Domain.Common;

namespace MealMate.Domain.VoiceCalls;

/// <summary>
/// A device-owned control session for the v2 voice-call protocol. The raw control
/// proof is returned exactly once at registration; only its SHA-256 hash is stored.
/// Rotating an installation revokes its previous sessions — a device id alone can
/// never control a call.
/// </summary>
public sealed class VoiceCallDeviceSession : AuditableEntity
{
    public Guid UserId { get; set; }

    /// <summary>Client installation identifier; re-registration rotates its sessions.</summary>
    public string InstallationId { get; set; } = default!;

    public string Platform { get; set; } = default!;

    /// <summary>Registered push token reference owned by this user (FCM registry row), when supplied.</summary>
    public Guid? FcmDeviceTokenId { get; set; }

    /// <summary>APNs VoIP device token for iOS incoming routing; never logged.</summary>
    public string? ApnsVoipToken { get; set; }

    /// <summary>SHA-256 hex of the per-session control proof; raw proof is never persisted.</summary>
    public string ControlProofHash { get; set; } = default!;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public int CapabilityVersion { get; set; }

    public DateTime LastSeenAtUtc { get; set; }

    public bool IsActive => RevokedAtUtc is null && DateTime.UtcNow < ExpiresAtUtc;
}
