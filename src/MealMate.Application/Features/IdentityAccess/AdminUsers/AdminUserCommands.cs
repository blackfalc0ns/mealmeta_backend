using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.AdminUsers;

public sealed record AdminUserDto(
    Guid Id,
    string? Email,
    string? FullName,
    AccountStatus AccountStatus,
    IReadOnlyList<string> Roles,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc);

public sealed record InviteAdminCommand(
    string Email,
    string FullName,
    string RoleName,
    Guid? AccessScopeId,
    string Reason) : ICommand<AdminUserDto>;

public sealed class InviteAdminCommandValidator : AbstractValidator<InviteAdminCommand>
{
    public InviteAdminCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.FullName).NotEmpty();
        RuleFor(x => x.RoleName).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty();
    }
}

public sealed class InviteAdminCommandHandler : ICommandHandler<InviteAdminCommand, AdminUserDto>
{
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IOtpService _otp;
    private readonly IDateTimeProvider _clock;

    public InviteAdminCommandHandler(
        IUserAccountService users,
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IOtpService otp,
        IDateTimeProvider clock)
    {
        _users = users;
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _otp = otp;
        _clock = clock;
    }

    public async Task<Result<AdminUserDto>> Handle(InviteAdminCommand request, CancellationToken cancellationToken)
    {
        if (!AppRoles.IsAdminRole(request.RoleName))
        {
            return Error.Validation(MessageCodes.Validation.Unknown, "Role is not an admin role.");
        }

        if (_currentUser.UserId is null)
        {
            return Error.Unauthorized(MessageCodes.Common.Unauthorized, "Not authenticated.");
        }

        if (AppRoles.IsSuperAdmin(request.RoleName) && !_currentUser.IsInRole(AppRoles.SuperAdmin))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Only SuperAdmin can invite SuperAdmin users.");
        }

        // Temporary password; invitee resets via OTP email flow
        var tempPassword = $"Tmp!{Guid.NewGuid():N}"[..16] + "Aa1";
        var create = await _users.CreateAsync(new CreateUserRequest(
            request.Email.Trim().ToLowerInvariant(),
            null,
            tempPassword,
            request.FullName,
            UserType.Admin,
            AccountStatus.Registered), cancellationToken);

        if (create.IsFailure)
        {
            return create.Error;
        }

        await _users.AddToRoleAsync(create.Value.Id, request.RoleName, cancellationToken);

        if (request.AccessScopeId is { } scopeId)
        {
            _db.UserScopes.Add(new UserScope
            {
                UserId = create.Value.Id,
                AccessScopeId = scopeId,
                CreatedAtUtc = _clock.UtcNow,
            });
        }

        await _otp.SendAsync(new OtpSendRequest(create.Value.Email!, "Email", "PasswordReset", create.Value.Id), cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "AdminUserInvited",
            "User",
            create.Value.Id,
            After: new { request.Email, request.RoleName, request.AccessScopeId },
            Reason: request.Reason,
            SourceChannel: "admin"), cancellationToken);

        return new AdminUserDto(
            create.Value.Id,
            create.Value.Email,
            create.Value.FullName,
            create.Value.AccountStatus,
            [request.RoleName],
            create.Value.CreatedAtUtc,
            create.Value.LastLoginAtUtc);
    }
}

public sealed record AcceptAdminInviteCommand(string Email, string OtpCode, string NewPassword) : ICommand;

public sealed class AcceptAdminInviteCommandHandler : ICommandHandler<AcceptAdminInviteCommand>
{
    private readonly IUserAccountService _users;
    private readonly IOtpService _otp;
    private readonly IAuditLogger _audit;

    public AcceptAdminInviteCommandHandler(IUserAccountService users, IOtpService otp, IAuditLogger audit)
    {
        _users = users;
        _otp = otp;
        _audit = audit;
    }

    public async Task<Result> Handle(AcceptAdminInviteCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var otp = await _otp.VerifyAsync(new OtpVerifyRequest(email, "PasswordReset", request.OtpCode), cancellationToken);
        if (otp.IsFailure)
        {
            return otp;
        }

        var user = await _users.FindByEmailAsync(email, cancellationToken);
        if (user is null || user.UserType != UserType.Admin)
        {
            return Error.NotFound(MessageCodes.Auth.InvitationInvalid, "Invitation not found.");
        }

        var token = await _users.GeneratePasswordResetTokenAsync(user.Id, cancellationToken);
        var reset = await _users.ResetPasswordAsync(user.Id, token, request.NewPassword, cancellationToken);
        if (reset.IsFailure)
        {
            return reset;
        }

        await _users.UpdateAccountStatusAsync(user.Id, AccountStatus.Active, cancellationToken);
        await _users.SetEmailConfirmedAsync(user.Id, true, cancellationToken);
        await _audit.LogAsync(new AuditEntry("AdminInviteAccepted", "User", user.Id, SourceChannel: "admin"), cancellationToken);
        return Result.Success();
    }
}

public sealed record SetAdminStatusCommand(Guid UserId, AccountStatus Status, string Reason) : ICommand;

public sealed class SetAdminStatusCommandHandler : ICommandHandler<SetAdminStatusCommand>
{
    private readonly IUserAccountService _users;
    private readonly ICurrentUser _currentUser;
    private readonly IAuthSessionService _sessions;
    private readonly IAuditLogger _audit;

    public SetAdminStatusCommandHandler(
        IUserAccountService users,
        ICurrentUser currentUser,
        IAuthSessionService sessions,
        IAuditLogger audit)
    {
        _users = users;
        _currentUser = currentUser;
        _sessions = sessions;
        _audit = audit;
    }

    public async Task<Result> Handle(SetAdminStatusCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId == request.UserId)
        {
            return Error.Forbidden(MessageCodes.Auth.SelfPrivilegeEscalation, "Cannot change your own status.");
        }

        var user = await _users.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null || user.UserType != UserType.Admin)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "Admin user not found.");
        }

        await _users.UpdateAccountStatusAsync(request.UserId, request.Status, cancellationToken);
        if (request.Status is AccountStatus.Suspended or AccountStatus.Closed)
        {
            await _sessions.RevokeAllUserTokensAsync(request.UserId, cancellationToken);
        }

        await _audit.LogAsync(new AuditEntry(
            "AdminStatusChanged",
            "User",
            request.UserId,
            Before: user.AccountStatus,
            After: request.Status,
            Reason: request.Reason,
            SourceChannel: "admin"), cancellationToken);

        return Result.Success();
    }
}

public sealed record ListAdminUsersQuery : IQuery<IReadOnlyList<AdminUserDto>>;

public sealed class ListAdminUsersQueryHandler : IQueryHandler<ListAdminUsersQuery, IReadOnlyList<AdminUserDto>>
{
    private readonly IUserAccountService _users;

    public ListAdminUsersQueryHandler(IUserAccountService users) => _users = users;

    public async Task<Result<IReadOnlyList<AdminUserDto>>> Handle(ListAdminUsersQuery request, CancellationToken cancellationToken)
    {
        var users = await _users.ListUsersByTypeAsync(UserType.Admin, cancellationToken);
        var result = new List<AdminUserDto>();
        foreach (var user in users)
        {
            var roles = await _users.GetRolesAsync(user.Id, cancellationToken);
            result.Add(new AdminUserDto(user.Id, user.Email, user.FullName, user.AccountStatus, roles, user.CreatedAtUtc, user.LastLoginAtUtc));
        }

        return result;
    }
}
