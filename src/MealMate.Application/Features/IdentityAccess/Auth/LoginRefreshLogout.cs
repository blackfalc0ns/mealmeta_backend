using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.IdentityAccess.Auth;

public sealed record LoginCommand(
    string? Email,
    string? Phone,
    string Password,
    string? DeviceInfo = null,
    string? IpAddress = null) : ICommand<LoginResultDto>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Password).NotEmpty();
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Email) || !string.IsNullOrWhiteSpace(x.Phone))
            .WithErrorCode(MessageCodes.Validation.Required);
    }
}

public sealed class LoginCommandHandler : ICommandHandler<LoginCommand, LoginResultDto>
{
    private readonly IUserAccountService _users;
    private readonly IAuthSessionService _sessions;
    private readonly IAuthLoginChallengeService _challenges;
    private readonly ITotpAuthenticatorService _totp;
    private readonly IAuditLogger _audit;
    private readonly IUnitOfWork _unitOfWork;

    public LoginCommandHandler(
        IUserAccountService users,
        IAuthSessionService sessions,
        IAuthLoginChallengeService challenges,
        ITotpAuthenticatorService totp,
        IAuditLogger audit,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _sessions = sessions;
        _challenges = challenges;
        _totp = totp;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<LoginResultDto>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        UserAccountDto? user = null;
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            user = await _users.FindByEmailAsync(request.Email.Trim().ToLowerInvariant(), cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            user = await _users.FindByPhoneAsync(request.Phone.Trim(), cancellationToken);
        }

        var passwordValid = await _users.VerifyPasswordOrDummyAsync(user?.Id, request.Password, cancellationToken);

        if (user is not null && await _users.IsLockedOutAsync(user.Id, cancellationToken))
        {
            await _audit.LogAsync(new AuditEntry(
                "LoginFailed",
                "User",
                user.Id,
                After: new { request.Email, request.Phone, Reason = "AccountLocked" },
                SourceChannel: "auth"), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "Invalid email/phone or password.");
        }

        if (!passwordValid)
        {
            if (user is not null)
            {
                await _users.RegisterFailedLoginAsync(user.Id, cancellationToken);
            }

            await _audit.LogAsync(new AuditEntry(
                "LoginFailed",
                "User",
                user?.Id,
                After: new { request.Email, request.Phone },
                SourceChannel: "auth"), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "Invalid email/phone or password.");
        }

        if (user is null)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "Invalid email/phone or password.");
        }

        if (user.AccountStatus == AccountStatus.Suspended)
        {
            return Error.Forbidden(MessageCodes.Auth.AccountSuspended, "Account is suspended.");
        }

        if (user.AccountStatus == AccountStatus.Closed)
        {
            return Error.Forbidden(MessageCodes.Auth.AccountClosed, "Account is closed.");
        }

        await _users.ResetAccessFailedCountAsync(user.Id, cancellationToken);

        if (user.UserType != UserType.Admin)
        {
            var session = await _sessions.IssueSessionAsync(user.Id, request.DeviceInfo, request.IpAddress, cancellationToken);
            if (session.IsFailure)
            {
                return session.Error;
            }

            await _audit.LogAsync(new AuditEntry(
                "UserLoggedIn",
                "User",
                user.Id,
                After: new { user.UserType },
                SourceChannel: "auth"), cancellationToken);

            return LoginResultDto.FromSession(AuthMapping.ToResponse(session.Value));
        }

        var totpEnabled = await _totp.IsEnabledAsync(user.Id, cancellationToken);
        var nextStep = totpEnabled ? LoginChallengeSteps.VerifyTotp : LoginChallengeSteps.EnrollTotp;
        var challenge = await _challenges.CreateAsync(
            new CreateLoginChallengeRequest(user.Id, nextStep, request.DeviceInfo, request.IpAddress),
            cancellationToken);
        var roles = await _users.GetRolesAsync(user.Id, cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "AdminLoginMfaRequired",
            "User",
            user.Id,
            After: new { nextStep },
            SourceChannel: "auth"), cancellationToken);

        return LoginResultDto.FromPendingChallenge(
            user,
            roles,
            challenge.RawToken,
            nextStep,
            challenge.ExpiresAtUtc);
    }
}

public sealed record RefreshTokenCommand(
    string RefreshToken,
    string? DeviceInfo = null,
    string? IpAddress = null) : ICommand<AuthResponseDto>;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

public sealed class RefreshTokenCommandHandler : ICommandHandler<RefreshTokenCommand, AuthResponseDto>
{
    private readonly IAuthSessionService _sessions;

    public RefreshTokenCommandHandler(IAuthSessionService sessions) => _sessions = sessions;

    public async Task<Result<AuthResponseDto>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var result = await _sessions.RefreshSessionAsync(request.RefreshToken, request.DeviceInfo, request.IpAddress, cancellationToken);
        return result.IsFailure ? result.Error : AuthMapping.ToResponse(result.Value);
    }
}

public sealed record LogoutCommand(string? RefreshToken) : ICommand;

public sealed class LogoutCommandHandler : ICommandHandler<LogoutCommand>
{
    private readonly IAuthSessionService _sessions;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public LogoutCommandHandler(
        IAuthSessionService sessions,
        ICurrentUser currentUser,
        IAuditLogger audit)
    {
        _sessions = sessions;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            await _sessions.RevokeRefreshTokenAsync(request.RefreshToken, cancellationToken);
        }
        else if (_currentUser.UserId is { } userId)
        {
            await _sessions.RevokeAllUserTokensAsync(userId, cancellationToken);
        }

        await _audit.LogAsync(new AuditEntry("UserLoggedOut", "User", _currentUser.UserId, SourceChannel: "auth"), cancellationToken);
        return Result.Success();
    }
}
