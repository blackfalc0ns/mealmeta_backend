using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;

namespace MealMate.Application.Features.IdentityAccess.Auth;

public sealed record SendOtpCommand(
    string Destination,
    string Channel,
    string Purpose) : ICommand;

public sealed class SendOtpCommandValidator : AbstractValidator<SendOtpCommand>
{
    public SendOtpCommandValidator()
    {
        RuleFor(x => x.Destination).NotEmpty();
        RuleFor(x => x.Channel).NotEmpty();
        RuleFor(x => x.Purpose).NotEmpty();
    }
}

public sealed class SendOtpCommandHandler : ICommandHandler<SendOtpCommand>
{
    private readonly IOtpService _otp;
    private readonly IUserAccountService _users;
    private readonly IAuditLogger _audit;
    private readonly ICurrentUser _currentUser;

    public SendOtpCommandHandler(IOtpService otp, IUserAccountService users, IAuditLogger audit, ICurrentUser currentUser)
    {
        _otp = otp;
        _users = users;
        _audit = audit;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(SendOtpCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is null && !OtpPurposes.IsAllowedForAnonymousSend(request.Purpose))
        {
            return Error.Validation(MessageCodes.Validation.Unknown, "Invalid OTP purpose.");
        }

        Guid? userId = _currentUser.UserId;
        if (userId is null)
        {
            var byEmail = request.Channel.Equals("Email", StringComparison.OrdinalIgnoreCase)
                ? await _users.FindByEmailAsync(request.Destination, cancellationToken)
                : null;
            var byPhone = request.Channel.Equals("Phone", StringComparison.OrdinalIgnoreCase)
                ? await _users.FindByPhoneAsync(request.Destination, cancellationToken)
                : null;
            userId = byEmail?.Id ?? byPhone?.Id;
        }

        var result = await _otp.SendAsync(new OtpSendRequest(
            request.Destination,
            request.Channel,
            request.Purpose,
            userId), cancellationToken);

        if (result.IsSuccess)
        {
            await _audit.LogAsync(new AuditEntry("OtpSent", "OtpChallenge", userId, After: new { request.Purpose, request.Channel }, SourceChannel: "auth"), cancellationToken);
        }

        return result;
    }
}

public sealed record VerifyOtpCommand(
    string Destination,
    string Purpose,
    string Code) : ICommand;

public sealed class VerifyOtpCommandValidator : AbstractValidator<VerifyOtpCommand>
{
    public VerifyOtpCommandValidator()
    {
        RuleFor(x => x.Destination).NotEmpty();
        RuleFor(x => x.Purpose).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().Length(6);
    }
}

public sealed class VerifyOtpCommandHandler : ICommandHandler<VerifyOtpCommand>
{
    private readonly IOtpService _otp;
    private readonly IUserAccountService _users;
    private readonly IAuditLogger _audit;

    public VerifyOtpCommandHandler(IOtpService otp, IUserAccountService users, IAuditLogger audit)
    {
        _otp = otp;
        _users = users;
        _audit = audit;
    }

    public async Task<Result> Handle(VerifyOtpCommand request, CancellationToken cancellationToken)
    {
        if (request.Purpose.Equals(OtpPurposes.PasswordReset, StringComparison.OrdinalIgnoreCase))
        {
            return Error.Validation(
                MessageCodes.Validation.Unknown,
                "Use the reset-password endpoint to verify a password-reset code.");
        }

        if (request.Purpose.Equals(OtpPurposes.AdminInvite, StringComparison.OrdinalIgnoreCase))
        {
            return Error.Validation(
                MessageCodes.Validation.Unknown,
                "Use the accept-invite endpoint to verify an admin invitation code.");
        }

        var result = await _otp.VerifyAsync(new OtpVerifyRequest(request.Destination, request.Purpose, request.Code), cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        var user = request.Destination.Contains('@')
            ? await _users.FindByEmailAsync(request.Destination.Trim().ToLowerInvariant(), cancellationToken)
            : await _users.FindByPhoneAsync(request.Destination.Trim(), cancellationToken);

        if (user is not null)
        {
            if (request.Purpose.Equals(OtpPurposes.VerifyEmail, StringComparison.OrdinalIgnoreCase))
            {
                await _users.SetEmailConfirmedAsync(user.Id, true, cancellationToken);
            }
            else if (request.Purpose.Equals(OtpPurposes.VerifyPhone, StringComparison.OrdinalIgnoreCase))
            {
                await _users.SetPhoneConfirmedAsync(user.Id, true, cancellationToken);
            }
        }

        await _audit.LogAsync(new AuditEntry("OtpVerified", "OtpChallenge", user?.Id, After: new { request.Purpose }, SourceChannel: "auth"), cancellationToken);
        return Result.Success();
    }
}

public sealed record ForgotPasswordCommand(string Email) : ICommand;

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress();
}

public sealed class ForgotPasswordCommandHandler : ICommandHandler<ForgotPasswordCommand>
{
    private readonly IUserAccountService _users;
    private readonly IOtpService _otp;
    private readonly IAuditLogger _audit;

    public ForgotPasswordCommandHandler(IUserAccountService users, IOtpService otp, IAuditLogger audit)
    {
        _users = users;
        _otp = otp;
        _audit = audit;
    }

    public async Task<Result> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _users.FindByEmailAsync(email, cancellationToken);

        // Always succeed to avoid account enumeration
        if (user is not null)
        {
            await _otp.SendAsync(new OtpSendRequest(email, "Email", OtpPurposes.PasswordReset, user.Id), cancellationToken);
            await _audit.LogAsync(new AuditEntry("PasswordResetRequested", "User", user.Id, SourceChannel: "auth"), cancellationToken);
        }

        return Result.Success();
    }
}

public sealed record ResetPasswordCommand(
    string Email,
    string OtpCode,
    string NewPassword) : ICommand;

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.OtpCode).NotEmpty().Length(6);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
    }
}

public sealed class ResetPasswordCommandHandler : ICommandHandler<ResetPasswordCommand>
{
    private readonly IUserAccountService _users;
    private readonly IOtpService _otp;
    private readonly IAuthSessionService _sessions;
    private readonly IAuditLogger _audit;

    public ResetPasswordCommandHandler(
        IUserAccountService users,
        IOtpService otp,
        IAuthSessionService sessions,
        IAuditLogger audit)
    {
        _users = users;
        _otp = otp;
        _sessions = sessions;
        _audit = audit;
    }

    public async Task<Result> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var otp = await _otp.VerifyAsync(new OtpVerifyRequest(email, OtpPurposes.PasswordReset, request.OtpCode), cancellationToken);
        if (otp.IsFailure)
        {
            return otp;
        }

        var user = await _users.FindByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "User not found.");
        }

        var token = await _users.GeneratePasswordResetTokenAsync(user.Id, cancellationToken);
        var reset = await _users.ResetPasswordAsync(user.Id, token, request.NewPassword, cancellationToken);
        if (reset.IsFailure)
        {
            return reset;
        }

        await _sessions.RevokeAllUserTokensAsync(user.Id, cancellationToken);
        await _audit.LogAsync(new AuditEntry("PasswordResetCompleted", "User", user.Id, SourceChannel: "auth"), cancellationToken);
        return Result.Success();
    }
}
