using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Services;

/// <summary>
/// Keeps live v2 voice-call device sessions pointing at the installation's current
/// push route. Matching is explicit — owner + installation id + platform, live
/// (non-revoked, unexpired) sessions only — and a missing match is never inferred
/// from "the most recent device". Session identity (id, control proof) and any
/// active call are left untouched; only the FCM registry reference moves.
/// </summary>
public static class VoiceCallPushRouteRebinder
{
    /// <summary>
    /// Per-(owner, installation) in-process gates serializing one registration's
    /// read-modify-write; the push-token rows' PostgreSQL xmin concurrency token is
    /// the cross-instance backstop behind this gate.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.SemaphoreSlim> RegistrationGates = new();

    public static System.Threading.SemaphoreSlim RegistrationGate(Guid userId, string installationId) =>
        RegistrationGates.GetOrAdd($"{userId:N}:{installationId}", _ => new System.Threading.SemaphoreSlim(1, 1));

    /// <summary>
    /// Rebinds the caller-owned live sessions of one installation to the given
    /// registry row. Must be invoked before the registration handler's SaveChanges
    /// so the route swap commits atomically with the token registration. Returns the
    /// rebound sessions so a concurrency retry can detach exactly what was touched.
    /// </summary>
    public static async Task<List<VoiceCallDeviceSession>> RebindLiveSessionsAsync(
        IAppDbContext db,
        Guid userId,
        string? installationId,
        MobilePlatform platform,
        Guid fcmDeviceTokenId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(installationId))
        {
            return []; // no explicit installation match — never guess
        }

        var sessions = await db.VoiceCallDeviceSessions
            .Where(s => s.UserId == userId
                        && s.InstallationId == installationId
                        && s.Platform == PlatformName(platform)
                        && s.RevokedAtUtc == null
                        && s.ExpiresAtUtc > nowUtc)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            if (session.FcmDeviceTokenId == fcmDeviceTokenId)
            {
                continue;
            }

            session.FcmDeviceTokenId = fcmDeviceTokenId;
            session.ModifiedAtUtc = nowUtc;
        }

        return sessions;
    }

    /// <summary>
    /// When a token row is claimed by a new account, the previous owner's live
    /// sessions must stop targeting it: the route reference is cleared (the session
    /// itself never moves between accounts). Returns the detached sessions.
    /// </summary>
    public static async Task<List<VoiceCallDeviceSession>> DetachRoutesClaimedByAnotherAccountAsync(
        IAppDbContext db,
        Guid fcmDeviceTokenId,
        Guid newOwnerId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var stale = await db.VoiceCallDeviceSessions
            .Where(s => s.FcmDeviceTokenId == fcmDeviceTokenId && s.UserId != newOwnerId)
            .ToListAsync(cancellationToken);

        foreach (var session in stale)
        {
            session.FcmDeviceTokenId = null;
            session.ModifiedAtUtc = nowUtc;
        }

        return stale;
    }

    private static string PlatformName(MobilePlatform platform) =>
        platform == MobilePlatform.Ios ? "ios" : "android";
}
