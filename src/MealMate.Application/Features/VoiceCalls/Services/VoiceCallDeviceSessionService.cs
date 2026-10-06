using System.Security.Cryptography;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.V2.Dtos;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Services;

/// <summary>
/// Persistent device-session store. Proof handling: 32 random bytes → base64url,
/// returned once, stored only as SHA-256 (constant-time verified on resolve).
/// Ownership checks are fail-closed with a single generic deny reason.
/// </summary>
public sealed class VoiceCallDeviceSessionService : IVoiceCallDeviceSessionService
{
    /// <summary>Proposed session lifetime; pending G-POLICY adoption.</summary>
    public static readonly TimeSpan DefaultSessionLifetime = TimeSpan.FromDays(30);

    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    /// <summary>
    /// Per-(user, installation) rotation gates. Serializes concurrent local
    /// registrations so the database partial-unique index is a backstop for
    /// cross-instance races, not a retry lottery inside one node.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.SemaphoreSlim> RotationGates = new();

    public VoiceCallDeviceSessionService(IAppDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<VoiceCallDeviceRegistrationResult>> RegisterAsync(
        Guid userId,
        VoiceCallDeviceRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return DeviceDeny();
        }

        if (string.IsNullOrWhiteSpace(request.InstallationId) || request.InstallationId.Length > 128)
        {
            return DeviceDeny();
        }

        var platform = request.Platform?.Trim().ToLowerInvariant();
        if (platform is not ("android" or "ios"))
        {
            return DeviceDeny();
        }

        // Push-token ownership: a token registered to another account can never be
        // claimed by this session. The APNs VoIP token is bound to this session only.
        if (request.FcmDeviceTokenId.HasValue &&
            !await OwnsFcmTokenAsync(userId, request.FcmDeviceTokenId.Value, cancellationToken))
        {
            return DeviceDeny();
        }

        var now = _clock.UtcNow;

        // G06 atomic rotation: the partial unique index (UserId, InstallationId) WHERE
        // RevokedAtUtc IS NULL is the database-level invariant. A racing registration
        // loses the insert, retries, revokes the winner it now sees, and re-inserts —
        // every request succeeds and exactly one active session survives.
        const int maxRotationAttempts = 5;
        var gate = RotationGates.GetOrAdd($"{userId:N}:{request.InstallationId}", _ => new System.Threading.SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await RotateWithRetryAsync(userId, request, platform!, now, maxRotationAttempts, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Result<VoiceCallDeviceRegistrationResult>> RotateWithRetryAsync(
        Guid userId,
        VoiceCallDeviceRegistrationRequest request,
        string platform,
        DateTime now,
        int maxRotationAttempts,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var stale = await _db.Set<VoiceCallDeviceSession>()
                .Where(s => s.UserId == userId
                            && s.InstallationId == request.InstallationId
                            && s.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var session in stale)
            {
                session.RevokedAtUtc = now;
            }

            var proof = GenerateProof();
            var entity = new VoiceCallDeviceSession
            {
                UserId = userId,
                InstallationId = request.InstallationId,
                Platform = platform!,
                FcmDeviceTokenId = request.FcmDeviceTokenId,
                ApnsVoipToken = string.IsNullOrWhiteSpace(request.ApnsVoipToken) ? null : request.ApnsVoipToken,
                ControlProofHash = HashProof(proof),
                ExpiresAtUtc = now.Add(DefaultSessionLifetime),
                CapabilityVersion = request.CapabilityVersion,
                LastSeenAtUtc = now
            };

            _db.Set<VoiceCallDeviceSession>().Add(entity);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (
                attempt < maxRotationAttempts - 1 &&
                IsPostgresUniqueViolation(ex))
            {
                // Another racing registration won this installation slot. Detach our
                // failed insert (a staged Added entity would violate the index again on
                // the next save), then rotate the winner we are about to re-read.
                _db.DetachTrackedEntity(entity);
                continue;
            }

            return Result.Success(new VoiceCallDeviceRegistrationResult(
                entity.Id,
                proof,
                entity.ExpiresAtUtc));
        }
    }

    public async Task<Result> RevokeAsync(Guid userId, Guid deviceSessionId, CancellationToken cancellationToken)
    {
        var session = await _db.Set<VoiceCallDeviceSession>()
            .FirstOrDefaultAsync(s => s.Id == deviceSessionId && s.UserId == userId, cancellationToken);

        if (session is null || session.RevokedAtUtc != null)
        {
            // Owner-scoped idempotency: another user's session is indistinguishable.
            return Result.Success();
        }

        session.RevokedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<CallControlContext>> ResolveAsync(
        Guid userId,
        Guid deviceSessionId,
        string controlProof,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || deviceSessionId == Guid.Empty || string.IsNullOrEmpty(controlProof))
        {
            return DenyContext();
        }

        var session = await _db.Set<VoiceCallDeviceSession>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == deviceSessionId, cancellationToken);

        if (session is null || session.UserId != userId)
        {
            return DenyContext();
        }

        if (session.RevokedAtUtc != null || session.ExpiresAtUtc <= _clock.UtcNow)
        {
            return DenyContext();
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(HashProof(controlProof)),
                Convert.FromHexString(session.ControlProofHash)))
        {
            return DenyContext();
        }

        return Result.Success(new CallControlContext(userId, session.Id));
    }

    private async Task<bool> OwnsFcmTokenAsync(Guid userId, Guid tokenId, CancellationToken cancellationToken)
    {
        var customerOwned = await _db.CustomerDeviceTokens
            .AnyAsync(t => t.Id == tokenId && t.UserId == userId && t.IsActive, cancellationToken);
        if (customerOwned)
        {
            return true;
        }

        var driverOwned = await _db.DriverDeviceTokens
            .AnyAsync(t => t.Id == tokenId && t.UserId == userId && t.IsActive, cancellationToken);
        return driverOwned;
    }

    /// <summary>
    /// PostgreSQL unique-violation check without a provider dependency in the
    /// Application layer: reflects SqlState off the PostgresException base error.
    /// </summary>
    private static bool IsPostgresUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is { } baseError &&
        baseError.GetType().Name == "PostgresException" &&
        baseError.GetType().GetProperty("SqlState")?.GetValue(baseError) is "23505";

    private static string GenerateProof()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string HashProof(string proof) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(proof)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static Result<VoiceCallDeviceRegistrationResult> DeviceDeny() =>
        Result.Failure<VoiceCallDeviceRegistrationResult>(
            Error.Forbidden("voice.device_registration_denied", "Device session registration was denied."));

    private static Result<CallControlContext> DenyContext() =>
        Result.Failure<CallControlContext>(
            Error.Forbidden("voice.device_control_denied", "Device control session is not valid."));
}
