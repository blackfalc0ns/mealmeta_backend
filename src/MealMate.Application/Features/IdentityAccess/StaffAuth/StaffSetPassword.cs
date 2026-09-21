using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Common.Validation;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.IdentityAccess.StaffAuth;

public sealed record StaffSetPasswordCommand(
    string Phone,
    string Role,
    string VerificationToken,
    string NewPassword,
    string ConfirmPassword,
    string? DeviceInfo = null,
    string? IpAddress = null) : ICommand<LoginResultDto>;

public sealed class StaffSetPasswordCommandValidator : AbstractValidator<StaffSetPasswordCommand>
{
    public StaffSetPasswordCommandValidator()
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

        RuleFor(x => x.VerificationToken)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required);

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

public sealed class StaffSetPasswordCommandHandler
    : ICommandHandler<StaffSetPasswordCommand, LoginResultDto>
{
    private readonly IUserAccountService _users;
    private readonly IAuthSessionService _sessions;
    private readonly IAuthLoginChallengeService _challenges;
    private readonly IAuditLogger _audit;
    private readonly IAppDbContext _db;

    public StaffSetPasswordCommandHandler(
        IUserAccountService users,
        IAuthSessionService sessions,
        IAuthLoginChallengeService challenges,
        IAuditLogger audit,
        IAppDbContext db)
    {
        _users = users;
        _sessions = sessions;
        _challenges = challenges;
        _audit = audit;
        _db = db;
    }

    public async Task<Result<LoginResultDto>> Handle(
        StaffSetPasswordCommand request,
        CancellationToken cancellationToken)
    {
        var phone = request.Phone.Trim();
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

        var resetResult = await _users.ResetPasswordAsync(user.Id, request.VerificationToken, request.NewPassword, cancellationToken);
        if (resetResult.IsFailure)
        {
            return resetResult.Error;
        }

        await _users.SetPhoneConfirmedAsync(user.Id, true, cancellationToken);
        await _users.UpdateAccountStatusAsync(user.Id, AccountStatus.Active, cancellationToken);
        await _users.SetHasPasswordAsync(user.Id, true, cancellationToken);
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
            "StaffFirstTimePasswordSetupCompleted",
            "User",
            user.Id,
            After: new { Phone = phone, Role = request.Role },
            SourceChannel: "staff_mobile_auth"), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return LoginResultDto.FromSession(AuthMapping.ToResponse(sessionResult.Value));
    }
}
