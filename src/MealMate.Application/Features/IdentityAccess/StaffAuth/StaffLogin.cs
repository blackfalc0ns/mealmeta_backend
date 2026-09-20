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

public sealed record StaffLoginCommand(
    string Phone,
    string Role,
    string Password,
    string? DeviceInfo = null,
    string? IpAddress = null) : ICommand<LoginResultDto>;

public sealed class StaffLoginCommandValidator : AbstractValidator<StaffLoginCommand>
{
    public StaffLoginCommandValidator()
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

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required);
    }
}

public sealed class StaffLoginCommandHandler : ICommandHandler<StaffLoginCommand, LoginResultDto>
{
    private readonly IUserAccountService _users;
    private readonly IAuthSessionService _sessions;
    private readonly IAuditLogger _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILocalizedMessageProvider _messages;

    public StaffLoginCommandHandler(
        IUserAccountService users,
        IAuthSessionService sessions,
        IAuditLogger audit,
        IUnitOfWork unitOfWork,
        ILocalizedMessageProvider messages)
    {
        _users = users;
        _sessions = sessions;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _messages = messages;
    }

    public async Task<Result<LoginResultDto>> Handle(
        StaffLoginCommand request,
        CancellationToken cancellationToken)
    {
        var phone = request.Phone.Trim();
        var user = await _users.FindByPhoneAsync(phone, cancellationToken);

        var passwordValid = user is not null && await _users.CheckPasswordAsync(user.Id, request.Password, cancellationToken);
        if (!passwordValid || user is null)
        {
            if (user is not null)
            {
                await _users.RegisterFailedLoginAsync(user.Id, cancellationToken);
            }
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "The phone number or password is incorrect.");
        }

        var expectedUserType = StaffRoles.ToUserType(request.Role);
        if (user.UserType != expectedUserType)
        {
            var actualRole = user.UserType == UserType.Driver
                ? _messages.GetMessage("roles.driver")
                : _messages.GetMessage("roles.delivery_manager");
            var requestedRole = expectedUserType == UserType.Driver
                ? _messages.GetMessage("roles.driver")
                : _messages.GetMessage("roles.delivery_manager");

            return Error.ValidationWithArguments(
                MessageCodes.Auth.RoleMismatchDetail,
                "Selected role does not match this account.",
                actualRole,
                requestedRole);
        }

        if (user.AccountStatus == AccountStatus.Suspended)
        {
            return Error.Forbidden(MessageCodes.Auth.AccountSuspended, "This account is suspended. Please contact administration.");
        }

        if (user.AccountStatus == AccountStatus.Closed)
        {
            return Error.Forbidden(MessageCodes.Auth.AccountClosed, "This account is closed.");
        }

        await _users.ResetAccessFailedCountAsync(user.Id, cancellationToken);
        await _users.UpdateLastLoginAsync(user.Id, cancellationToken);

        var session = await _sessions.IssueSessionAsync(user.Id, request.DeviceInfo, request.IpAddress, cancellationToken);
        if (session.IsFailure)
        {
            return session.Error;
        }

        await _audit.LogAsync(new AuditEntry(
            "StaffLoggedIn",
            "User",
            user.Id,
            After: new { Role = request.Role, Phone = phone },
            SourceChannel: "staff_mobile_auth"), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return LoginResultDto.FromSession(AuthMapping.ToResponse(session.Value));
    }
}
