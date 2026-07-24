using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;

namespace MealMate.Application.Common.Abstractions;

public sealed record AuthTokensDto(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);

public sealed record AuthSessionDto(
    Guid UserId,
    string? Email,
    string? PhoneNumber,
    string? FullName,
    UserType UserType,
    AccountStatus AccountStatus,
    Guid? RestaurantId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    AuthTokensDto Tokens);

public interface IAuthSessionService
{
    Task<AuthSessionDto> IssueSessionAsync(Guid userId, string? deviceInfo = null, string? ipAddress = null, CancellationToken cancellationToken = default);

    Task<Result<AuthSessionDto>> RefreshSessionAsync(string refreshToken, string? deviceInfo = null, string? ipAddress = null, CancellationToken cancellationToken = default);

    Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task RevokeAllUserTokensAsync(Guid userId, CancellationToken cancellationToken = default);
}
