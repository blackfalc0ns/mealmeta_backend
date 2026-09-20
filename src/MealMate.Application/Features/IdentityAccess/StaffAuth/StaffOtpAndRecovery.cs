using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Common.Validation;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.IdentityAccess.StaffAuth;

public sealed record StaffResendOtpCommand(string Phone, string Role) : ICommand;

public sealed class StaffResendOtpCommandValidator : AbstractValidator<StaffResendOtpCommand>
{
    public StaffResendOtpCommandValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required)
            .MaximumLength(30);

        RuleFor(x => x.Role)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required)
            .Must(StaffRoles.IsValid)
            .WithMessageCode(MessageCodes.Auth.InvalidStaffRole);
    }
}

public sealed class StaffResendOtpCommandHandler : ICommandHandler<StaffResendOtpCommand>
{
    private readonly IUserAccountService _users;
    private readonly IOtpService _otp;
    private readonly IAuditLogger _audit;

    public StaffResendOtpCommandHandler(
        IUserAccountService users,
        IOtpService otp,
        IAuditLogger audit)
    {
        _users = users;
        _otp = otp;
        _audit = audit;
    }

    public async Task<Result> Handle(StaffResendOtpCommand request, CancellationToken cancellationToken)
    {
        var phone = request.Phone.Trim();
        var user = await _users.FindByPhoneAsync(phone, cancellationToken);
        if (user is null)
        {
            // Return success or generic not to leak user info
            return Result.Success();
        }

        var expectedUserType = StaffRoles.ToUserType(request.Role);
        if (user.UserType != expectedUserType)
        {
            return Error.Validation(MessageCodes.Auth.RoleMismatch, "The selected role does not match this account type.");
        }

        var sendResult = await _otp.SendAsync(new OtpSendRequest(
            phone,
            "Phone",
            OtpPurposes.VerifyPhone,
            user.Id), cancellationToken);

        if (sendResult.IsFailure)
        {
            return sendResult;
        }

        await _audit.LogAsync(new AuditEntry(
            "StaffPhoneOtpResent",
            "User",
            user.Id,
            After: new { Role = request.Role, Purpose = OtpPurposes.VerifyPhone },
            SourceChannel: "staff_mobile_auth"), cancellationToken);

        return Result.Success();
    }
}

public sealed record StaffForgotPasswordCommand(string Phone, string Role) : ICommand;

public sealed class StaffForgotPasswordCommandValidator : AbstractValidator<StaffForgotPasswordCommand>
{
    public StaffForgotPasswordCommandValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required)
            .MaximumLength(30);

        RuleFor(x => x.Role)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required)
            .Must(StaffRoles.IsValid)
            .WithMessageCode(MessageCodes.Auth.InvalidStaffRole);
    }
}

public sealed class StaffForgotPasswordCommandHandler : ICommandHandler<StaffForgotPasswordCommand>
{
    private readonly IUserAccountService _users;
    private readonly IOtpService _otp;
    private readonly IAuditLogger _audit;

    public StaffForgotPasswordCommandHandler(
        IUserAccountService users,
        IOtpService otp,
        IAuditLogger audit)
    {
        _users = users;
        _otp = otp;
        _audit = audit;
    }

    public async Task<Result> Handle(StaffForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var phone = request.Phone.Trim();
        var user = await _users.FindByPhoneAsync(phone, cancellationToken);
        if (user is null)
        {
            return Result.Success();
        }

        var expectedUserType = StaffRoles.ToUserType(request.Role);
        if (user.UserType != expectedUserType)
        {
            return Error.Validation(MessageCodes.Auth.RoleMismatch, "The selected role does not match this account type.");
        }

        var sendResult = await _otp.SendAsync(new OtpSendRequest(
            phone,
            "SMS",
            OtpPurposes.PasswordReset,
            user.Id), cancellationToken);

        if (sendResult.IsFailure)
        {
            return sendResult;
        }

        await _audit.LogAsync(new AuditEntry(
            "StaffForgotPasswordRequested",
            "User",
            user.Id,
            After: new { Role = request.Role },
            SourceChannel: "staff_mobile_auth"), cancellationToken);

        return Result.Success();
    }
}

public sealed record StaffResetPasswordCommand(
    string Phone,
    string Role,
    string OtpCode,
    string NewPassword,
    string ConfirmPassword) : ICommand;

public sealed class StaffResetPasswordCommandValidator : AbstractValidator<StaffResetPasswordCommand>
{
    public StaffResetPasswordCommandValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required)
            .MaximumLength(30);

        RuleFor(x => x.Role)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required)
            .Must(StaffRoles.IsValid)
            .WithMessageCode(MessageCodes.Auth.InvalidStaffRole);

        RuleFor(x => x.OtpCode)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required)
            .Length(4, 8)
            .WithMessageCode(MessageCodes.Validation.OtpCodeInvalid);

        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required)
            .MinimumLength(8)
            .WithMessageCode(MessageCodes.Validation.PasswordMinimumLength);

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.NewPassword)
            .WithMessageCode(MessageCodes.Validation.PasswordMismatch);
    }
}

public sealed class StaffResetPasswordCommandHandler : ICommandHandler<StaffResetPasswordCommand>
{
    private readonly IUserAccountService _users;
    private readonly IOtpService _otp;
    private readonly IAuthSessionService _sessions;
    private readonly IAuthLoginChallengeService _challenges;
    private readonly IAuditLogger _audit;
    private readonly IUnitOfWork _unitOfWork;

    public StaffResetPasswordCommandHandler(
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

    public async Task<Result> Handle(StaffResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var phone = request.Phone.Trim();
        var otpResult = await _otp.VerifyAsync(
            new OtpVerifyRequest(phone, OtpPurposes.PasswordReset, request.OtpCode.Trim()),
            cancellationToken);

        if (otpResult.IsFailure)
        {
            return otpResult;
        }

        var user = await _users.FindByPhoneAsync(phone, cancellationToken);
        if (user is null)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "User not found.");
        }

        var expectedUserType = StaffRoles.ToUserType(request.Role);
        if (user.UserType != expectedUserType)
        {
            return Error.Validation(MessageCodes.Auth.RoleMismatch, "The selected role does not match this account type.");
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
            "StaffPasswordResetCompleted",
            "User",
            user.Id,
            After: new { Role = request.Role },
            SourceChannel: "staff_mobile_auth"), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
