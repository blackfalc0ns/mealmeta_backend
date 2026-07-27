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

/// <summary>
/// Login outcome: either a full authenticated session or a pending admin MFA challenge.
/// Non-admin successful logins populate session fields; pending admin MFA omits tokens.
/// </summary>
public sealed record LoginResultDto(
    Guid? UserId,
    string? Email,
    string? PhoneNumber,
    string? FullName,
    UserType? UserType,
    AccountStatus? AccountStatus,
    Guid? RestaurantId,
    IReadOnlyList<string>? Roles,
    IReadOnlyList<string>? Permissions,
    string? AccessToken,
    DateTime? AccessTokenExpiresAtUtc,
    string? RefreshToken,
    DateTime? RefreshTokenExpiresAtUtc,
    string? NextStep,
    string? LoginChallengeToken,
    DateTime? LoginChallengeExpiresAtUtc,
    IReadOnlyList<string>? RecoveryCodes = null)
{
    public bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);

    public static LoginResultDto FromSession(AuthResponseDto session, IReadOnlyList<string>? recoveryCodes = null) => new(
        session.UserId,
        session.Email,
        session.PhoneNumber,
        session.FullName,
        session.UserType,
        session.AccountStatus,
        session.RestaurantId,
        session.Roles,
        session.Permissions,
        session.AccessToken,
        session.AccessTokenExpiresAtUtc,
        session.RefreshToken,
        session.RefreshTokenExpiresAtUtc,
        session.NextStep,
        null,
        null,
        recoveryCodes);

    public static LoginResultDto FromPendingChallenge(
        UserAccountDto user,
        IReadOnlyList<string> roles,
        string challengeToken,
        string nextStep,
        DateTime challengeExpiresAtUtc) => new(
        user.Id,
        user.Email,
        user.PhoneNumber,
        user.FullName,
        user.UserType,
        user.AccountStatus,
        user.RestaurantId,
        roles,
        null,
        null,
        null,
        null,
        null,
        nextStep,
        challengeToken,
        challengeExpiresAtUtc);
}

public sealed record TotpSetupResponseDto(
    string SharedKey,
    string AuthenticatorUri,
    string Issuer,
    string AccountName);

public sealed record TotpConfirmSetupResponseDto(
    IReadOnlyList<string> RecoveryCodes,
    LoginResultDto Session);

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
