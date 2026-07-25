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

    public async Task<AuthSessionDto> IssueSessionAsync(
        Guid userId,
        string? deviceInfo = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken)
                   ?? throw new InvalidOperationException($"User {userId} not found.");

        var roles = await _users.GetRolesAsync(userId, cancellationToken);
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

        var active = await _db.RefreshTokens
            .FirstOrDefaultAsync(x => x.Id == existing.Id, cancellationToken);

        if (active is null || active.RevokedAtUtc is not null || active.ExpiresAtUtc <= _clock.UtcNow)
        {
            await transaction.RollbackAsync(cancellationToken);
            if (active?.RevokedAtUtc is not null)
            {
                await RevokeAllUserTokensAsync(existing.UserId, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
            }

            return Error.Unauthorized(MessageCodes.Auth.InvalidRefreshToken, "Refresh token is invalid or expired.");
        }

        var roles = await _users.GetRolesAsync(user.Id, cancellationToken);
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
            UserId = active.UserId,
            TokenHash = HashToken(newRaw),
            CreatedAtUtc = _clock.UtcNow,
            ExpiresAtUtc = _clock.UtcNow.AddDays(_jwt.RefreshTokenDays),
            DeviceInfo = deviceInfo ?? active.DeviceInfo,
            IpAddress = ipAddress ?? active.IpAddress,
        };

        active.RevokedAtUtc = _clock.UtcNow;
        active.ReplacedByTokenId = newEntity.Id;
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

    internal static string HashToken(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
