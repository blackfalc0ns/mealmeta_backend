using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.IdentityAccess.Auth;

public sealed record TotpSetupCommand(string LoginChallengeToken) : ICommand<TotpSetupResponseDto>;

public sealed class TotpSetupCommandValidator : AbstractValidator<TotpSetupCommand>
{
    public TotpSetupCommandValidator() => RuleFor(x => x.LoginChallengeToken).NotEmpty();
}

public sealed class TotpSetupCommandHandler : ICommandHandler<TotpSetupCommand, TotpSetupResponseDto>
{
    private readonly IAuthLoginChallengeService _challenges;
    private readonly ITotpAuthenticatorService _totp;
    private readonly IUserAccountService _users;
    private readonly IAuditLogger _audit;

    public TotpSetupCommandHandler(
        IAuthLoginChallengeService challenges,
        ITotpAuthenticatorService totp,
        IUserAccountService users,
        IAuditLogger audit)
    {
        _challenges = challenges;
        _totp = totp;
        _users = users;
        _audit = audit;
    }

    public async Task<Result<TotpSetupResponseDto>> Handle(TotpSetupCommand request, CancellationToken cancellationToken)
    {
        var challenge = await _challenges.ResolveAsync(
            request.LoginChallengeToken,
            LoginChallengeSteps.EnrollTotp,
            cancellationToken);
        if (challenge.IsFailure)
        {
            return challenge.Error;
        }

        var user = await _users.FindByIdAsync(challenge.Value.UserId, cancellationToken);
        if (user is null || user.UserType != UserType.Admin)
        {
            return Error.Validation(MessageCodes.Auth.TotpSetupNotAllowed, "TOTP setup is not allowed.");
        }

        if (await _totp.IsEnabledAsync(user.Id, cancellationToken))
        {
            return Error.Validation(MessageCodes.Auth.TotpSetupNotAllowed, "TOTP is already enabled.");
        }

        var setup = await _totp.GetOrCreateSetupAsync(user.Id, cancellationToken);
        await _audit.LogAsync(new AuditEntry(
            "TotpEnrollmentStarted",
            "User",
            user.Id,
            SourceChannel: "auth"), cancellationToken);

        return new TotpSetupResponseDto(
            setup.SharedKey,
            setup.AuthenticatorUri,
            setup.Issuer,
            setup.AccountName);
    }
}

public sealed record TotpConfirmSetupCommand(string LoginChallengeToken, string Code) : ICommand<TotpConfirmSetupResponseDto>;

public sealed class TotpConfirmSetupCommandValidator : AbstractValidator<TotpConfirmSetupCommand>
{
    public TotpConfirmSetupCommandValidator()
    {
        RuleFor(x => x.LoginChallengeToken).NotEmpty();
        RuleFor(x => x.Code).NotEmpty();
    }
}

public sealed class TotpConfirmSetupCommandHandler : ICommandHandler<TotpConfirmSetupCommand, TotpConfirmSetupResponseDto>
{
    private readonly IAuthLoginChallengeService _challenges;
    private readonly ITotpAuthenticatorService _totp;
    private readonly IAuthSessionService _sessions;
    private readonly IUserAccountService _users;
    private readonly IAuditLogger _audit;

    public TotpConfirmSetupCommandHandler(
        IAuthLoginChallengeService challenges,
        ITotpAuthenticatorService totp,
        IAuthSessionService sessions,
        IUserAccountService users,
        IAuditLogger audit)
    {
        _challenges = challenges;
        _totp = totp;
        _sessions = sessions;
        _users = users;
        _audit = audit;
    }

    public async Task<Result<TotpConfirmSetupResponseDto>> Handle(
        TotpConfirmSetupCommand request,
        CancellationToken cancellationToken)
    {
        var challenge = await _challenges.ResolveAsync(
            request.LoginChallengeToken,
            LoginChallengeSteps.EnrollTotp,
            cancellationToken);
        if (challenge.IsFailure)
        {
            return challenge.Error;
        }

        var resolved = challenge.Value;
        var user = await _users.FindByIdAsync(resolved.UserId, cancellationToken);
        if (user is null || user.UserType != UserType.Admin)
        {
            return Error.Validation(MessageCodes.Auth.TotpSetupNotAllowed, "TOTP setup is not allowed.");
        }

        if (!await _totp.VerifyCodeAsync(resolved.UserId, request.Code, cancellationToken))
        {
            await _challenges.RecordFailedAttemptAsync(resolved, cancellationToken);
            await _audit.LogAsync(new AuditEntry(
                "TotpVerificationFailed",
                "User",
                user.Id,
                After: new { UsedRecoveryCode = false },
                SourceChannel: "auth"), cancellationToken);

            if (resolved.AttemptCount + 1 >= resolved.MaxAttempts)
            {
                await _users.RegisterFailedLoginAsync(user.Id, cancellationToken);
            }

            return Error.Validation(MessageCodes.Auth.TotpInvalid, "Invalid authenticator code.");
        }

        var consumed = await _challenges.TryConsumeAsync(resolved, cancellationToken);
        if (consumed.IsFailure)
        {
            return consumed.Error;
        }

        var enrollment = await _totp.CompleteEnrollmentAsync(resolved.UserId, cancellationToken);
        if (enrollment.IsFailure)
        {
            return enrollment.Error;
        }

        var recoveryCodes = enrollment.Value;
        var session = await _sessions.IssueSessionAsync(resolved.UserId, cancellationToken: cancellationToken);
        if (session.IsFailure)
        {
            return session.Error;
        }

        await _audit.LogAsync(new AuditEntry(
            "TotpEnrollmentCompleted",
            "User",
            user.Id,
            After: new { RecoveryCodeCount = recoveryCodes.Count },
            SourceChannel: "auth"), cancellationToken);
        await _audit.LogAsync(new AuditEntry(
            "UserLoggedIn",
            "User",
            user.Id,
            After: new { user.UserType, MfaMethod = "totp" },
            SourceChannel: "auth"), cancellationToken);

        return new TotpConfirmSetupResponseDto(
            recoveryCodes,
            LoginResultDto.FromSession(AuthMapping.ToResponse(session.Value), recoveryCodes));
    }
}

public sealed record TotpVerifyLoginCommand(
    string LoginChallengeToken,
    string? Code,
    string? RecoveryCode,
    string? DeviceInfo = null,
    string? IpAddress = null) : ICommand<LoginResultDto>;

public sealed class TotpVerifyLoginCommandValidator : AbstractValidator<TotpVerifyLoginCommand>
{
    public TotpVerifyLoginCommandValidator()
    {
        RuleFor(x => x.LoginChallengeToken).NotEmpty();
        RuleFor(x => x)
            .Must(x => HasExactlyOneCredential(x.Code, x.RecoveryCode))
            .WithErrorCode(MessageCodes.Validation.Required);
    }

    private static bool HasExactlyOneCredential(string? code, string? recoveryCode)
    {
        var hasCode = !string.IsNullOrWhiteSpace(code);
        var hasRecovery = !string.IsNullOrWhiteSpace(recoveryCode);
        return hasCode ^ hasRecovery;
    }
}

public sealed class TotpVerifyLoginCommandHandler : ICommandHandler<TotpVerifyLoginCommand, LoginResultDto>
{
    private readonly IAuthLoginChallengeService _challenges;
    private readonly ITotpAuthenticatorService _totp;
    private readonly IAuthSessionService _sessions;
    private readonly IUserAccountService _users;
    private readonly IAuditLogger _audit;

    public TotpVerifyLoginCommandHandler(
        IAuthLoginChallengeService challenges,
        ITotpAuthenticatorService totp,
        IAuthSessionService sessions,
        IUserAccountService users,
        IAuditLogger audit)
    {
        _challenges = challenges;
        _totp = totp;
        _sessions = sessions;
        _users = users;
        _audit = audit;
    }

    public async Task<Result<LoginResultDto>> Handle(TotpVerifyLoginCommand request, CancellationToken cancellationToken)
    {
        var challenge = await _challenges.ResolveAsync(
            request.LoginChallengeToken,
            LoginChallengeSteps.VerifyTotp,
            cancellationToken);
        if (challenge.IsFailure)
        {
            return challenge.Error;
        }

        var resolved = challenge.Value;
        var user = await _users.FindByIdAsync(resolved.UserId, cancellationToken);
        if (user is null || user.UserType != UserType.Admin)
        {
            return Error.Validation(MessageCodes.Auth.TotpInvalid, "Invalid authenticator code.");
        }

        var usedRecoveryCode = !string.IsNullOrWhiteSpace(request.RecoveryCode);
        var verified = usedRecoveryCode
            ? await _totp.RedeemRecoveryCodeAsync(resolved.UserId, request.RecoveryCode!, cancellationToken)
            : await _totp.VerifyCodeAsync(resolved.UserId, request.Code!, cancellationToken);

        if (!verified)
        {
            await _challenges.RecordFailedAttemptAsync(resolved, cancellationToken);
            await _audit.LogAsync(new AuditEntry(
                "TotpVerificationFailed",
                "User",
                user.Id,
                After: new { usedRecoveryCode },
                SourceChannel: "auth"), cancellationToken);

            if (resolved.AttemptCount + 1 >= resolved.MaxAttempts)
            {
                await _users.RegisterFailedLoginAsync(user.Id, cancellationToken);
            }

            return Error.Validation(MessageCodes.Auth.TotpInvalid, "Invalid authenticator code.");
        }

        var consumed = await _challenges.TryConsumeAsync(resolved, cancellationToken);
        if (consumed.IsFailure)
        {
            return consumed.Error;
        }

        var session = await _sessions.IssueSessionAsync(
            resolved.UserId,
            request.DeviceInfo,
            request.IpAddress,
            cancellationToken);
        if (session.IsFailure)
        {
            return session.Error;
        }

        await _audit.LogAsync(new AuditEntry(
            usedRecoveryCode ? "TotpRecoveryCodeUsed" : "TotpLoginVerified",
            "User",
            user.Id,
            SourceChannel: "auth"), cancellationToken);
        await _audit.LogAsync(new AuditEntry(
            "UserLoggedIn",
            "User",
            user.Id,
            After: new { user.UserType, MfaMethod = usedRecoveryCode ? "recovery_code" : "totp" },
            SourceChannel: "auth"), cancellationToken);

        return LoginResultDto.FromSession(AuthMapping.ToResponse(session.Value));
    }
}
