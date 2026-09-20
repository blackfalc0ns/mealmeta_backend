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

public sealed record StaffVerifyFirstTimeOtpCommand(
    string Phone,
    string Role,
    string OtpCode) : ICommand<StaffVerifyFirstTimeOtpDto>;

public sealed class StaffVerifyFirstTimeOtpCommandValidator : AbstractValidator<StaffVerifyFirstTimeOtpCommand>
{
    public StaffVerifyFirstTimeOtpCommandValidator()
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
    }
}

public sealed class StaffVerifyFirstTimeOtpCommandHandler
    : ICommandHandler<StaffVerifyFirstTimeOtpCommand, StaffVerifyFirstTimeOtpDto>
{
    private readonly IUserAccountService _users;
    private readonly IOtpService _otp;
    private readonly IAuditLogger _audit;
    private readonly ILocalizedMessageProvider _messages;

    public StaffVerifyFirstTimeOtpCommandHandler(
        IUserAccountService users,
        IOtpService otp,
        IAuditLogger audit,
        ILocalizedMessageProvider messages)
    {
        _users = users;
        _otp = otp;
        _audit = audit;
        _messages = messages;
    }

    public async Task<Result<StaffVerifyFirstTimeOtpDto>> Handle(
        StaffVerifyFirstTimeOtpCommand request,
        CancellationToken cancellationToken)
    {
        var phone = request.Phone.Trim();
        var otpResult = await _otp.VerifyAsync(
            new OtpVerifyRequest(phone, OtpPurposes.VerifyPhone, request.OtpCode.Trim()),
            cancellationToken);

        if (otpResult.IsFailure)
        {
            return otpResult.Error;
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

        var verificationToken = await _users.GeneratePasswordResetTokenAsync(user.Id, cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "StaffOtpVerifiedForSetup",
            "User",
            user.Id,
            After: new { Phone = phone, Role = request.Role },
            SourceChannel: "staff_mobile_auth"), cancellationToken);

        return new StaffVerifyFirstTimeOtpDto(
            Verified: true,
            VerificationToken: verificationToken,
            Phone: phone,
            Role: request.Role,
            Message: _messages.GetMessage(MessageCodes.Auth.OtpVerifiedSetPassword));
    }
}
