using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.V2.Dtos;

namespace MealMate.Application.Features.VoiceCalls.Abstractions;

/// <summary>
/// Device-control session lifecycle for the v2 protocol (spec §7.1): registration
/// binds sessions to the authenticated owner, issues the control proof exactly once
/// (only its hash is stored), and rotation/revocation cuts off prior sessions.
/// </summary>
public interface IVoiceCallDeviceSessionService
{
    Task<Result<VoiceCallDeviceRegistrationResult>> RegisterAsync(
        Guid userId,
        VoiceCallDeviceRegistrationRequest request,
        CancellationToken cancellationToken);

    /// <summary>Owner-scoped revocation; idempotent. Never leaks whether the id exists for another user.</summary>
    Task<Result> RevokeAsync(Guid userId, Guid deviceSessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Validates user ownership, revocation, expiry, and the control proof
    /// (constant-time hash comparison). Any failure returns a generic deny.
    /// </summary>
    Task<Result<CallControlContext>> ResolveAsync(
        Guid userId,
        Guid deviceSessionId,
        string controlProof,
        CancellationToken cancellationToken);
}

public sealed record VoiceCallDeviceRegistrationRequest(
    string InstallationId,
    string Platform,
    int CapabilityVersion,
    Guid? FcmDeviceTokenId = null,
    string? ApnsVoipToken = null);

public sealed record VoiceCallDeviceRegistrationResult(
    Guid DeviceSessionId,
    string ControlProof,
    DateTime ExpiresAtUtc);
