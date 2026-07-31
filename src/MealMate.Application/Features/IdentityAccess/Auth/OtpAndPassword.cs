using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;

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
    private readonly IAuthLoginChallengeService _challenges;
    private readonly IAuditLogger _audit;
    private readonly IUnitOfWork _unitOfWork;

    public ResetPasswordCommandHandler(
        IUserAccountService users,
        IOtpService otp,
        IAuthSessionService sessions,
        IAuthLoginChallengeService challenges,
        IAuditLogger audit,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _otp = otp;
        _sessions = sessions;
        _challenges = challenges;
        _audit = audit;
        _unitOfWork = unitOfWork;
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
        await _challenges.InvalidateAllForUserAsync(user.Id, cancellationToken);
        await _audit.LogAsync(new AuditEntry("PasswordResetCompleted", "User", user.Id, SourceChannel: "auth"), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// -- Customer-specific phone OTP commands ------------------------------------------

public sealed record CustomerVerifyPhoneCommand(
    string Phone,
    string Code,
    string? DeviceInfo = null,
    string? IpAddress = null) : ICommand<AuthResponseDto>;

public sealed class CustomerVerifyPhoneCommandValidator : AbstractValidator<CustomerVerifyPhoneCommand>
{
    public CustomerVerifyPhoneCommandValidator()
    {
        RuleFor(x => x.Phone).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().Length(6);
    }
}

public sealed class CustomerVerifyPhoneCommandHandler : ICommandHandler<CustomerVerifyPhoneCommand, AuthResponseDto>
{
    private readonly IOtpService _otp;
    private readonly IUserAccountService _users;
    private readonly IAuthSessionService _sessions;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public CustomerVerifyPhoneCommandHandler(
        IOtpService otp,
        IUserAccountService users,
        IAuthSessionService sessions,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _otp = otp;
        _users = users;
        _sessions = sessions;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<AuthResponseDto>> Handle(CustomerVerifyPhoneCommand request, CancellationToken cancellationToken)
    {
        var phone = request.Phone.Trim();

        var otpResult = await _otp.VerifyAsync(
            new OtpVerifyRequest(phone, OtpPurposes.VerifyPhone, request.Code), cancellationToken);
        if (otpResult.IsFailure)
        {
            return otpResult.Error;
        }

        var user = await _users.FindByPhoneAsync(phone, cancellationToken);
        if (user is null || user.UserType != UserType.Customer)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "User not found.");
        }

        await _users.SetPhoneConfirmedAsync(user.Id, true, cancellationToken);

        var session = await _sessions.IssueSessionAsync(user.Id, request.DeviceInfo, request.IpAddress, cancellationToken);
        if (session.IsFailure)
        {
            return session.Error;
        }

        await _audit.LogAsync(new AuditEntry(
            "CustomerPhoneVerified",
            nameof(CustomerProfile),
            user.Id,
            After: new { user.PhoneNumber },
            SourceChannel: "customer-app"), cancellationToken);

        return AuthMapping.ToResponse(session.Value, "choose_subscription");
    }
}

public sealed record CustomerResendPhoneOtpCommand(string Phone) : ICommand;

public sealed class CustomerResendPhoneOtpCommandValidator : AbstractValidator<CustomerResendPhoneOtpCommand>
{
    public CustomerResendPhoneOtpCommandValidator() => RuleFor(x => x.Phone).NotEmpty();
}

public sealed class CustomerResendPhoneOtpCommandHandler : ICommandHandler<CustomerResendPhoneOtpCommand>
{
    private readonly IUserAccountService _users;
    private readonly IOtpService _otp;
    private readonly IAuditLogger _audit;

    public CustomerResendPhoneOtpCommandHandler(
        IUserAccountService users,
        IOtpService otp,
        IAuditLogger audit)
    {
        _users = users;
        _otp = otp;
        _audit = audit;
    }

    public async Task<Result> Handle(CustomerResendPhoneOtpCommand request, CancellationToken cancellationToken)
    {
        var phone = request.Phone.Trim();
        var user = await _users.FindByPhoneAsync(phone, cancellationToken);

        // Generic success to avoid account enumeration
        if (user is null || user.UserType != UserType.Customer)
        {
            return Result.Success();
        }

        var sendResult = await _otp.SendAsync(new OtpSendRequest(phone, "WhatsApp", OtpPurposes.VerifyPhone, user.Id), cancellationToken);
        if (sendResult.IsFailure)
        {
            return sendResult;
        }

        await _audit.LogAsync(new AuditEntry(
            "CustomerPhoneOtpResent",
            "User",
            user.Id,
            After: new { Purpose = OtpPurposes.VerifyPhone, Channel = "WhatsApp" },
            SourceChannel: "auth"), cancellationToken);

        return Result.Success();
    }
}

public sealed record CustomerForgotPasswordCommand(string Phone) : ICommand;

public sealed class CustomerForgotPasswordCommandValidator : AbstractValidator<CustomerForgotPasswordCommand>
{
    public CustomerForgotPasswordCommandValidator() => RuleFor(x => x.Phone).NotEmpty();
}

public sealed class CustomerForgotPasswordCommandHandler : ICommandHandler<CustomerForgotPasswordCommand>
{
    private readonly IUserAccountService _users;
    private readonly IOtpService _otp;
    private readonly IAuditLogger _audit;

    public CustomerForgotPasswordCommandHandler(
        IUserAccountService users,
        IOtpService otp,
        IAuditLogger audit)
    {
        _users = users;
        _otp = otp;
        _audit = audit;
    }

    public async Task<Result> Handle(CustomerForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var phone = request.Phone.Trim();
        var user = await _users.FindByPhoneAsync(phone, cancellationToken);

        // Generic success to avoid account enumeration
        if (user is not null && user.UserType == UserType.Customer)
        {
            var sendResult = await _otp.SendAsync(
                new OtpSendRequest(phone, "WhatsApp", OtpPurposes.PasswordReset, user.Id), cancellationToken);
            if (sendResult.IsFailure)
            {
                return sendResult;
            }

            await _audit.LogAsync(new AuditEntry(
                "CustomerPasswordResetRequested",
                "User",
                user.Id,
                SourceChannel: "auth"), cancellationToken);
        }

        return Result.Success();
    }
}

public sealed record CustomerResetPasswordCommand(
    string Phone,
    string OtpCode,
    string NewPassword) : ICommand;

public sealed class CustomerResetPasswordCommandValidator : AbstractValidator<CustomerResetPasswordCommand>
{
    public CustomerResetPasswordCommandValidator()
    {
        RuleFor(x => x.Phone).NotEmpty();
        RuleFor(x => x.OtpCode).NotEmpty().Length(6);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
    }
}

public sealed class CustomerResetPasswordCommandHandler : ICommandHandler<CustomerResetPasswordCommand>
{
    private readonly IUserAccountService _users;
    private readonly IOtpService _otp;
    private readonly IAuthSessionService _sessions;
    private readonly IAuthLoginChallengeService _challenges;
    private readonly IAuditLogger _audit;
    private readonly IUnitOfWork _unitOfWork;

    public CustomerResetPasswordCommandHandler(
        IUserAccountService users,
        IOtpService otp,
        IAuthSessionService sessions,
        IAuthLoginChallengeService challenges,
        IAuditLogger audit,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _otp = otp;
        _sessions = sessions;
        _challenges = challenges;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(CustomerResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var phone = request.Phone.Trim();

        var otpResult = await _otp.VerifyAsync(
            new OtpVerifyRequest(phone, OtpPurposes.PasswordReset, request.OtpCode), cancellationToken);
        if (otpResult.IsFailure)
        {
            return otpResult;
        }

        var user = await _users.FindByPhoneAsync(phone, cancellationToken);
        if (user is null || user.UserType != UserType.Customer)
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
        await _challenges.InvalidateAllForUserAsync(user.Id, cancellationToken);
        await _audit.LogAsync(new AuditEntry(
            "CustomerPasswordResetCompleted",
            "User",
            user.Id,
            SourceChannel: "auth"), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record DriverForgotPasswordCommand(string Phone) : ICommand;

public sealed class DriverForgotPasswordCommandValidator : AbstractValidator<DriverForgotPasswordCommand>
{
    public DriverForgotPasswordCommandValidator() => RuleFor(x => x.Phone).NotEmpty();
}

public sealed class DriverForgotPasswordCommandHandler : ICommandHandler<DriverForgotPasswordCommand>
{
    private readonly IUserAccountService _users;
    private readonly IOtpService _otp;
    private readonly IAuditLogger _audit;

    public DriverForgotPasswordCommandHandler(IUserAccountService users, IOtpService otp, IAuditLogger audit)
    {
        _users = users;
        _otp = otp;
        _audit = audit;
    }

    public async Task<Result> Handle(DriverForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var phone = request.Phone.Trim();
        var user = await _users.FindByPhoneAsync(phone, cancellationToken);

        if (user is not null && user.UserType == UserType.Driver)
        {
            var sendResult = await _otp.SendAsync(
                new OtpSendRequest(phone, "SMS", OtpPurposes.PasswordReset, user.Id), cancellationToken);
            if (sendResult.IsFailure) return sendResult;

            await _audit.LogAsync(new AuditEntry("DriverPasswordResetRequested", "User", user.Id, SourceChannel: "auth"), cancellationToken);
        }

        return Result.Success();
    }
}

public sealed record DriverResetPasswordCommand(string Phone, string OtpCode, string NewPassword) : ICommand;

public sealed class DriverResetPasswordCommandValidator : AbstractValidator<DriverResetPasswordCommand>
{
    public DriverResetPasswordCommandValidator()
    {
        RuleFor(x => x.Phone).NotEmpty();
        RuleFor(x => x.OtpCode).NotEmpty().Length(6);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
    }
}

public sealed class DriverResetPasswordCommandHandler : ICommandHandler<DriverResetPasswordCommand>
{
    private readonly IUserAccountService _users;
    private readonly IOtpService _otp;
    private readonly IAuthSessionService _sessions;
    private readonly IAuthLoginChallengeService _challenges;
    private readonly IAuditLogger _audit;
    private readonly IUnitOfWork _unitOfWork;

    public DriverResetPasswordCommandHandler(
        IUserAccountService users,
        IOtpService otp,
        IAuthSessionService sessions,
        IAuthLoginChallengeService challenges,
        IAuditLogger audit,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _otp = otp;
        _sessions = sessions;
        _challenges = challenges;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DriverResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var phone = request.Phone.Trim();
        var otpResult = await _otp.VerifyAsync(
            new OtpVerifyRequest(phone, OtpPurposes.PasswordReset, request.OtpCode), cancellationToken);
        if (otpResult.IsFailure) return otpResult;

        var user = await _users.FindByPhoneAsync(phone, cancellationToken);
        if (user is null || user.UserType != UserType.Driver)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "User not found.");
        }

        var token = await _users.GeneratePasswordResetTokenAsync(user.Id, cancellationToken);
        var reset = await _users.ResetPasswordAsync(user.Id, token, request.NewPassword, cancellationToken);
        if (reset.IsFailure) return reset;

        await _sessions.RevokeAllUserTokensAsync(user.Id, cancellationToken);
        await _challenges.InvalidateAllForUserAsync(user.Id, cancellationToken);
        await _audit.LogAsync(new AuditEntry("DriverPasswordResetCompleted", "User", user.Id, SourceChannel: "auth"), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record DeliveryManagerForgotPasswordCommand(string Email) : ICommand;

public sealed class DeliveryManagerForgotPasswordCommandValidator : AbstractValidator<DeliveryManagerForgotPasswordCommand>
{
    public DeliveryManagerForgotPasswordCommandValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress();
}

public sealed class DeliveryManagerForgotPasswordCommandHandler : ICommandHandler<DeliveryManagerForgotPasswordCommand>
{
    private readonly IUserAccountService _users;
    private readonly IOtpService _otp;
    private readonly IAuditLogger _audit;

    public DeliveryManagerForgotPasswordCommandHandler(IUserAccountService users, IOtpService otp, IAuditLogger audit)
    {
        _users = users;
        _otp = otp;
        _audit = audit;
    }

    public async Task<Result> Handle(DeliveryManagerForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _users.FindByEmailAsync(email, cancellationToken);

        if (user is not null && user.UserType == UserType.DeliveryManager)
        {
            var sendResult = await _otp.SendAsync(
                new OtpSendRequest(email, "Email", OtpPurposes.PasswordReset, user.Id), cancellationToken);
            if (sendResult.IsFailure) return sendResult;

            await _audit.LogAsync(new AuditEntry("DeliveryManagerPasswordResetRequested", "User", user.Id, SourceChannel: "auth"), cancellationToken);
        }

        return Result.Success();
    }
}

public sealed record DeliveryManagerResetPasswordCommand(string Email, string OtpCode, string NewPassword) : ICommand;

public sealed class DeliveryManagerResetPasswordCommandValidator : AbstractValidator<DeliveryManagerResetPasswordCommand>
{
    public DeliveryManagerResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.OtpCode).NotEmpty().Length(6);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
    }
}

public sealed class DeliveryManagerResetPasswordCommandHandler : ICommandHandler<DeliveryManagerResetPasswordCommand>
{
    private readonly IUserAccountService _users;
    private readonly IOtpService _otp;
    private readonly IAuthSessionService _sessions;
    private readonly IAuthLoginChallengeService _challenges;
    private readonly IAuditLogger _audit;
    private readonly IUnitOfWork _unitOfWork;

    public DeliveryManagerResetPasswordCommandHandler(
        IUserAccountService users,
        IOtpService otp,
        IAuthSessionService sessions,
        IAuthLoginChallengeService challenges,
        IAuditLogger audit,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _otp = otp;
        _sessions = sessions;
        _challenges = challenges;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeliveryManagerResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var otpResult = await _otp.VerifyAsync(
            new OtpVerifyRequest(email, OtpPurposes.PasswordReset, request.OtpCode), cancellationToken);
        if (otpResult.IsFailure) return otpResult;

        var user = await _users.FindByEmailAsync(email, cancellationToken);
        if (user is null || user.UserType != UserType.DeliveryManager)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "User not found.");
        }

        var token = await _users.GeneratePasswordResetTokenAsync(user.Id, cancellationToken);
        var reset = await _users.ResetPasswordAsync(user.Id, token, request.NewPassword, cancellationToken);
        if (reset.IsFailure) return reset;

        await _sessions.RevokeAllUserTokensAsync(user.Id, cancellationToken);
        await _challenges.InvalidateAllForUserAsync(user.Id, cancellationToken);
        await _audit.LogAsync(new AuditEntry("DeliveryManagerPasswordResetCompleted", "User", user.Id, SourceChannel: "auth"), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

