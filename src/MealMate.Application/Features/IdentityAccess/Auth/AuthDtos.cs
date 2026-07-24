using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.IdentityAccess.Auth;

public sealed record AuthResponseDto(
    Guid UserId,
    string? Email,
    string? PhoneNumber,
    string? FullName,
    UserType UserType,
    AccountStatus AccountStatus,
    Guid? RestaurantId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    string? NextStep = null);

public static class AuthMapping
{
    public static AuthResponseDto ToResponse(AuthSessionDto session, string? nextStep = null) => new(
        session.UserId,
        session.Email,
        session.PhoneNumber,
        session.FullName,
        session.UserType,
        session.AccountStatus,
        session.RestaurantId,
        session.Roles,
        session.Permissions,
        session.Tokens.AccessToken,
        session.Tokens.AccessTokenExpiresAtUtc,
        session.Tokens.RefreshToken,
        session.Tokens.RefreshTokenExpiresAtUtc,
        nextStep);
}
