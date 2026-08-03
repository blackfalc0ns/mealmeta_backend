using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MealMate.Persistence.Identity;

/// <summary>
/// Manages JWT access tokens and refresh-token-backed sessions.
/// </summary>
/// <remarks>
/// <para>
/// Access tokens are short-lived JWTs (default 15 min) and are intentionally stateless —
/// they are <b>not</b> revoked on logout or password reset. This is an accepted trade-off
/// between security and performance; the 15-minute window limits exposure. Refresh tokens
/// are revoked immediately on logout, password reset, or reuse detection.
/// </para>
/// <para>
/// Role and permission changes propagate to the client on the next token refresh (within 15 min).
/// Admin TOTP is verified only at password login (and enrollment); refresh rotation does not
/// re-prompt for TOTP while the refresh token remains valid.
/// </para>
/// </remarks>
public sealed class AuthSessionService : IAuthSessionService
{
    private readonly IUserAccountService _users;
    private readonly ITokenService _tokenService;
    private readonly AppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly JwtOptions _jwt;

    public AuthSessionService(
        IUserAccountService users,
        ITokenService tokenService,
        AppDbContext db,
        IDateTimeProvider clock,
        IOptions<JwtOptions> jwt)
    {
        _users = users;
        _tokenService = tokenService;
        _db = db;
        _clock = clock;
        _jwt = jwt.Value;
    }

    public async Task<Result<AuthSessionDto>> IssueSessionAsync(
        Guid userId,
        string? deviceInfo = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "User not found.");
        }

        if (user.AccountStatus == AccountStatus.Suspended)
        {
            return Error.Forbidden(MessageCodes.Auth.AccountSuspended, "Account is suspended.");
        }

        if (user.AccountStatus == AccountStatus.Closed)
        {
            return Error.Forbidden(MessageCodes.Auth.AccountClosed, "Account is closed.");
        }

        var roles = await LoadEffectiveRolesAsync(userId, cancellationToken);
        var permissions = await _users.GetPermissionsAsync(userId, cancellationToken);

        var access = _tokenService.CreateAccessToken(new TokenUser(
            user.Id,
            user.UserType,
            roles,
            permissions,
            user.RestaurantId));

        var refreshRaw = _tokenService.GenerateRefreshToken();
        var refreshExpires = _clock.UtcNow.AddDays(_jwt.RefreshTokenDays);

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = HashToken(refreshRaw),
            CreatedAtUtc = _clock.UtcNow,
            ExpiresAtUtc = refreshExpires,
            DeviceInfo = deviceInfo,
            IpAddress = ipAddress,
        });

        await _users.UpdateLastLoginAsync(userId, cancellationToken);

        return BuildSession(user, roles, permissions, access, refreshRaw, refreshExpires);
    }

    public async Task<Result<AuthSessionDto>> RefreshSessionAsync(
        string refreshToken,
        string? deviceInfo = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var hash = HashToken(refreshToken);
        var existing = await _db.RefreshTokens
            .FirstOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);

        if (existing is null)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidRefreshToken, "Refresh token is invalid or expired.");
        }

        if (existing.RevokedAtUtc is not null)
        {
            await RevokeAllUserTokensAsync(existing.UserId, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return Error.Unauthorized(MessageCodes.Auth.InvalidRefreshToken, "Refresh token is invalid or expired.");
        }

        if (existing.ExpiresAtUtc <= _clock.UtcNow)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidRefreshToken, "Refresh token is invalid or expired.");
        }

        var user = await _users.FindByIdAsync(existing.UserId, cancellationToken);
        if (user is null)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidRefreshToken, "Refresh token is invalid or expired.");
        }

        if (user.AccountStatus == AccountStatus.Suspended)
        {
            return Error.Forbidden(MessageCodes.Auth.AccountSuspended, "Account is suspended.");
        }

        if (user.AccountStatus == AccountStatus.Closed)
        {
            return Error.Forbidden(MessageCodes.Auth.AccountClosed, "Account is closed.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var now = _clock.UtcNow;
        var revoked = await _db.RefreshTokens
            .Where(x => x.Id == existing.Id && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.RevokedAtUtc, now),
                cancellationToken);

        if (revoked == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            await RevokeAllUserTokensAsync(existing.UserId, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return Error.Unauthorized(MessageCodes.Auth.InvalidRefreshToken, "Refresh token is invalid or expired.");
        }

        // ExecuteUpdateAsync bypasses the change tracker; sync so SaveChanges
        // cannot overwrite the CAS revocation with a stale null RevokedAtUtc.
        existing.RevokedAtUtc = now;

        var roles = await LoadEffectiveRolesAsync(user.Id, cancellationToken);
        var permissions = await _users.GetPermissionsAsync(user.Id, cancellationToken);
        var access = _tokenService.CreateAccessToken(new TokenUser(
            user.Id,
            user.UserType,
            roles,
            permissions,
            user.RestaurantId));

        var newRaw = _tokenService.GenerateRefreshToken();
        var newEntity = new RefreshToken
        {
            UserId = existing.UserId,
            TokenHash = HashToken(newRaw),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(_jwt.RefreshTokenDays),
            DeviceInfo = deviceInfo ?? existing.DeviceInfo,
            IpAddress = ipAddress ?? existing.IpAddress,
        };

        existing.ReplacedByTokenId = newEntity.Id;
        _db.RefreshTokens.Add(newEntity);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return BuildSession(user, roles, permissions, access, newRaw, newEntity.ExpiresAtUtc);
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(refreshToken);
        var existing = await _db.RefreshTokens
            .FirstOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);

        if (existing is null || existing.RevokedAtUtc is not null)
        {
            return;
        }

        existing.RevokedAtUtc = _clock.UtcNow;
    }

    public async Task RevokeAllUserTokensAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var tokens = await _db.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAtUtc = _clock.UtcNow;
        }
    }

    private static AuthSessionDto BuildSession(
        UserAccountDto user,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> permissions,
        AccessToken access,
        string refreshRaw,
        DateTime refreshExpires)
        => new(
            user.Id,
            user.Email,
            user.PhoneNumber,
            user.FullName,
            user.UserType,
            user.AccountStatus,
            user.RestaurantId,
            roles,
            permissions,
            new AuthTokensDto(access.Value, access.ExpiresAtUtc, refreshRaw, refreshExpires));

    private async Task<IReadOnlyList<string>> LoadEffectiveRolesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var identityRoles = await _users.GetRolesAsync(userId, cancellationToken);
        var branchRoles = await _db.BranchStaffAssignments
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId && assignment.IsActive)
            .Select(assignment => assignment.RoleId)
            .Where(roleId => roleId != null && roleId != "")
            .Distinct()
            .ToListAsync(cancellationToken);

        return identityRoles
            .Concat(branchRoles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static string HashToken(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
