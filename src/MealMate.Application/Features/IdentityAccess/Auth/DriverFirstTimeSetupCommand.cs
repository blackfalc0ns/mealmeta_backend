using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.IdentityAccess.Auth;

public sealed record DriverFirstTimeSetupCommand(
    string Phone,
    string OtpCode,
    string Password,
    string? DeviceInfo = null,
    string? IpAddress = null) : ICommand<LoginResultDto>;

public sealed class DriverFirstTimeSetupCommandValidator : AbstractValidator<DriverFirstTimeSetupCommand>
{
    public DriverFirstTimeSetupCommandValidator()
    {
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30);
        RuleFor(x => x.OtpCode).NotEmpty().Length(6);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}

public sealed class DriverFirstTimeSetupCommandHandler
    : ICommandHandler<DriverFirstTimeSetupCommand, LoginResultDto>
{
    private readonly IUserAccountService _users;
    private readonly IOtpService _otp;
    private readonly IAuthSessionService _sessions;
    private readonly IAuthLoginChallengeService _challenges;
    private readonly IAuditLogger _audit;
    private readonly IUnitOfWork _unitOfWork;

    public DriverFirstTimeSetupCommandHandler(
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

    public async Task<Result<LoginResultDto>> Handle(
        DriverFirstTimeSetupCommand request,
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
        if (user is null || user.UserType != UserType.Driver)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "Driver user not found.");
        }

        var token = await _users.GeneratePasswordResetTokenAsync(user.Id, cancellationToken);
        var resetResult = await _users.ResetPasswordAsync(user.Id, token, request.Password, cancellationToken);
        if (resetResult.IsFailure)
        {
            return resetResult.Error;
        }

        await _users.SetPhoneConfirmedAsync(user.Id, true, cancellationToken);
        await _users.UpdateLastLoginAsync(user.Id, cancellationToken);
        await _challenges.InvalidateAllForUserAsync(user.Id, cancellationToken);

        var sessionResult = await _sessions.IssueSessionAsync(
            user.Id,
            request.DeviceInfo,
            request.IpAddress,
            cancellationToken);

        if (sessionResult.IsFailure)
        {
            return sessionResult.Error;
        }

        await _audit.LogAsync(new AuditEntry(
            "DriverFirstTimeSetupCompleted",
            "User",
            user.Id,
            After: new { Phone = phone },
            SourceChannel: "mobile_auth"), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return LoginResultDto.FromSession(AuthMapping.ToResponse(sessionResult.Value));
    }
}
