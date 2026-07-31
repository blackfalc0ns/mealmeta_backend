using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MealMate.Infrastructure.Auth;

/// <summary>Mints signed JWT access tokens and cryptographically-random refresh tokens.</summary>
public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly IDateTimeProvider _dateTime;

    public TokenService(IOptions<JwtOptions> options, IDateTimeProvider dateTime)
    {
        _options = options.Value;
        _dateTime = dateTime;
    }

    public AccessToken CreateAccessToken(TokenUser user)
    {
        var expiresAt = _dateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(AppClaimTypes.UserId, user.UserId.ToString()),
            new(AppClaimTypes.UserType, user.UserType.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        if (user.RestaurantId is { } restaurantId)
        {
            claims.Add(new Claim(AppClaimTypes.RestaurantId, restaurantId.ToString()));
        }

        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(user.Permissions.Select(p => new Claim(AppClaimTypes.Permission, p)));

        if (user.ScopeIds is { Count: > 0 } scopeIds)
        {
            claims.AddRange(scopeIds.Select(id => new Claim(AppClaimTypes.ScopeId, id.ToString())));
        }

        if (user.TenantIds is { Count: > 0 } tenantIds)
        {
            claims.AddRange(tenantIds.Select(id => new Claim(AppClaimTypes.TenantId, id.ToString())));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: _dateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: credentials);

        var value = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessToken(value, expiresAt);
    }

    public string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }
}
