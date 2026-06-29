using MealMate.Domain.Enums;

namespace MealMate.Application.Common.Abstractions;

public sealed record TokenUser(
    Guid UserId,
    UserType UserType,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions,
    Guid? RestaurantId = null);

public sealed record AccessToken(
    string Value,
    DateTime ExpiresAtUtc);

/// <summary>
/// Issues signed JWT access tokens. Refresh-token persistence/rotation lives with
/// the identity feature; this only mints the signed access token from claims.
/// </summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(TokenUser user);

    string GenerateRefreshToken();
}
