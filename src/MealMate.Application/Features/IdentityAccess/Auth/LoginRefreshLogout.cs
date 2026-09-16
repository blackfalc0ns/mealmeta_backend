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

// -- User-type-specific login commands -------------------------------------------------
// These endpoints restrict login to a specific UserType and reject mismatches.

internal static class TypeSpecificLoginHelper
{
    public static async Task<Result<LoginResultDto>> ExecuteAsync(
        IUserAccountService users,
        IAuthSessionService sessions,
        IAuditLogger audit,
        IUnitOfWork unitOfWork,
        string? email,
        string? phone,
        string password,
        UserType expectedType,
        string? deviceInfo,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        UserAccountDto? user = null;
        if (!string.IsNullOrWhiteSpace(email))
        {
            user = await users.FindByEmailAsync(email.Trim().ToLowerInvariant(), cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(phone))
        {
            user = await users.FindByPhoneAsync(phone.Trim(), cancellationToken);
        }

        var passwordValid = await users.VerifyPasswordOrDummyAsync(user?.Id, password, cancellationToken);

        if (user is not null && await users.IsLockedOutAsync(user.Id, cancellationToken))
        {
            await audit.LogAsync(new AuditEntry(
                "LoginFailed", "User", user.Id,
                After: new { email, phone, Reason = "AccountLocked" },
                SourceChannel: "auth"), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "Invalid email/phone or password.");
        }

        if (!passwordValid)
        {
            if (user is not null)
                await users.RegisterFailedLoginAsync(user.Id, cancellationToken);

            await audit.LogAsync(new AuditEntry(
                "LoginFailed", "User", user?.Id,
                After: new { email, phone },
                SourceChannel: "auth"), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "Invalid email/phone or password.");
        }

        if (user is null)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "Invalid email/phone or password.");
        }

        // Type check: must match expected type (Influencers are Customer accounts with an Influencer profile)
        var isAllowedType = user.UserType == expectedType
            || (expectedType == UserType.Customer && user.UserType == UserType.Influencer);

        if (!isAllowedType)
        {
            await audit.LogAsync(new AuditEntry(
                "LoginFailedWrongType", "User", user.Id,
                After: new { email, phone, Expected = expectedType.ToString(), Actual = user.UserType.ToString() },
                SourceChannel: "auth"), cancellationToken);
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "Invalid email/phone or password.");
        }

        // Auto-heal legacy accounts that were mistakenly marked as UserType.Influencer
        if (user.UserType == UserType.Influencer)
        {
            await users.SetUserTypeAsync(user.Id, UserType.Customer, cancellationToken);
            await users.AddToRoleAsync(user.Id, AppRoles.Customer, cancellationToken);
            await users.AddToRoleAsync(user.Id, AppRoles.Influencer, cancellationToken);
            user = user with { UserType = UserType.Customer };
        }

        if (user.AccountStatus == AccountStatus.Suspended)
            return Error.Forbidden(MessageCodes.Auth.AccountSuspended, "Account is suspended.");

        if (user.AccountStatus == AccountStatus.Closed)
            return Error.Forbidden(MessageCodes.Auth.AccountClosed, "Account is closed.");

        if (user.AccountStatus == AccountStatus.PhonePending)
            return Error.Forbidden(MessageCodes.Auth.InvalidState, "Phone not confirmed. Please verify your phone number.");

        await users.ResetAccessFailedCountAsync(user.Id, cancellationToken);

        var session = await sessions.IssueSessionAsync(user.Id, deviceInfo, ipAddress, cancellationToken);
        if (session.IsFailure)
            return session.Error;

        await audit.LogAsync(new AuditEntry(
            "UserLoggedIn", "User", user.Id,
            After: new { user.UserType },
            SourceChannel: "auth"), cancellationToken);

        return LoginResultDto.FromSession(AuthMapping.ToResponse(session.Value));
    }
}

public sealed record CustomerLoginCommand(
    string? Email,
    string? Phone,
    string Password,
    string? DeviceInfo = null,
    string? IpAddress = null) : ICommand<LoginResultDto>;

public sealed class CustomerLoginCommandHandler : ICommandHandler<CustomerLoginCommand, LoginResultDto>
{
    private readonly IUserAccountService _users;
    private readonly IAuthSessionService _sessions;
    private readonly IAuditLogger _audit;
    private readonly IUnitOfWork _unitOfWork;

    public CustomerLoginCommandHandler(
        IUserAccountService users,
        IAuthSessionService sessions,
        IAuditLogger audit,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _sessions = sessions;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<LoginResultDto>> Handle(CustomerLoginCommand request, CancellationToken cancellationToken)
        => await TypeSpecificLoginHelper.ExecuteAsync(
            _users, _sessions, _audit, _unitOfWork,
            request.Email, request.Phone, request.Password,
            UserType.Customer, request.DeviceInfo, request.IpAddress, cancellationToken);
}

public sealed record DriverLoginCommand(
    string? Email,
    string? Phone,
    string Password,
    string? DeviceInfo = null,
    string? IpAddress = null) : ICommand<LoginResultDto>;

public sealed class DriverLoginCommandHandler : ICommandHandler<DriverLoginCommand, LoginResultDto>
{
    private readonly IUserAccountService _users;
    private readonly IAuthSessionService _sessions;
    private readonly IAuditLogger _audit;
    private readonly IUnitOfWork _unitOfWork;

    public DriverLoginCommandHandler(
        IUserAccountService users,
        IAuthSessionService sessions,
        IAuditLogger audit,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _sessions = sessions;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<LoginResultDto>> Handle(DriverLoginCommand request, CancellationToken cancellationToken)
        => await TypeSpecificLoginHelper.ExecuteAsync(
            _users, _sessions, _audit, _unitOfWork,
            request.Email, request.Phone, request.Password,
            UserType.Driver, request.DeviceInfo, request.IpAddress, cancellationToken);
}

public sealed record DeliveryManagerLoginCommand(
    string? Email,
    string? Phone,
    string Password,
    string? DeviceInfo = null,
    string? IpAddress = null) : ICommand<LoginResultDto>;

public sealed class DeliveryManagerLoginCommandHandler : ICommandHandler<DeliveryManagerLoginCommand, LoginResultDto>
{
    private readonly IUserAccountService _users;
    private readonly IAuthSessionService _sessions;
    private readonly IAuditLogger _audit;
    private readonly IUnitOfWork _unitOfWork;

    public DeliveryManagerLoginCommandHandler(
        IUserAccountService users,
        IAuthSessionService sessions,
        IAuditLogger audit,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _sessions = sessions;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<LoginResultDto>> Handle(DeliveryManagerLoginCommand request, CancellationToken cancellationToken)
    {
        var user = !string.IsNullOrWhiteSpace(request.Email)
            ? await _users.FindByEmailAsync(request.Email.Trim(), cancellationToken)
            : !string.IsNullOrWhiteSpace(request.Phone)
                ? await _users.FindByPhoneAsync(request.Phone.Trim(), cancellationToken)
                : null;

        var passwordValid = user is not null && await _users.CheckPasswordAsync(user.Id, request.Password, cancellationToken);
        if (!passwordValid || user is null || user.UserType != UserType.DeliveryManager)
        {
            if (user is not null) await _users.RegisterFailedLoginAsync(user.Id, cancellationToken);
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "Invalid email/phone or password.");
        }

        if (user.AccountStatus == AccountStatus.Suspended)
            return Error.Forbidden(MessageCodes.Auth.AccountSuspended, "Account is suspended.");

        if (user.AccountStatus == AccountStatus.Closed)
            return Error.Forbidden(MessageCodes.Auth.AccountClosed, "Account is closed.");

        await _users.ResetAccessFailedCountAsync(user.Id, cancellationToken);

        var isFirstLogin = user.LastLoginAtUtc is null;
        var nextStep = isFirstLogin ? "change_password" : null;

        var session = await _sessions.IssueSessionAsync(user.Id, request.DeviceInfo, request.IpAddress, cancellationToken);
        if (session.IsFailure)
            return session.Error;

        if (!isFirstLogin)
        {
            await _users.UpdateLastLoginAsync(user.Id, cancellationToken);
        }

        await _audit.LogAsync(new AuditEntry(
            "DeliveryManagerLoggedIn", "User", user.Id,
            After: new { isFirstLogin, nextStep },
            SourceChannel: "delivery_app"), cancellationToken);

        return LoginResultDto.FromSession(AuthMapping.ToResponse(session.Value, nextStep));
    }
}
