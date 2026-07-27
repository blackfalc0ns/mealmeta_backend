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
    bool EmailConfirmed,
    bool TwoFactorEnabled,
    IReadOnlyList<string> Roles,
    IReadOnlyList<AdminUserScopeDto> Scopes,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc);

public sealed record AdminUserScopeDto(Guid Id, string Name, AccessScopeType ScopeType);

public sealed record AdminInvitationDto(
    Guid UserId,
    string Email,
    string? FullName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<AdminUserScopeDto> Scopes,
    DateTime InvitedAtUtc);

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

        AccessScope? requestedScope = null;
        if (request.AccessScopeId is { } requestedScopeId)
        {
            requestedScope = await _db.AccessScopes.FirstOrDefaultAsync(
                scope => scope.Id == requestedScopeId && scope.IsActive,
                cancellationToken);

            if (requestedScope is null)
            {
                return Error.NotFound(MessageCodes.Auth.InvalidState, "Access scope not found.");
            }
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

        var roleAssignment = await _users.AddToRoleAsync(create.Value.Id, request.RoleName, cancellationToken);
        if (roleAssignment.IsFailure)
        {
            return roleAssignment.Error;
        }

        if (request.AccessScopeId is { } scopeId)
        {
            _db.UserScopes.Add(new UserScope
            {
                UserId = create.Value.Id,
                AccessScopeId = scopeId,
                CreatedAtUtc = _clock.UtcNow,
            });
        }

        var invitationSent = await _otp.SendAsync(
            new OtpSendRequest(create.Value.Email!, "Email", OtpPurposes.AdminInvite, create.Value.Id),
            cancellationToken);
        if (invitationSent.IsFailure)
        {
            return invitationSent.Error;
        }

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
            create.Value.EmailConfirmed,
            false,
            [request.RoleName],
            requestedScope is null
                ? Array.Empty<AdminUserScopeDto>()
                : [new AdminUserScopeDto(requestedScope.Id, requestedScope.Name, requestedScope.ScopeType)],
            create.Value.CreatedAtUtc,
            create.Value.LastLoginAtUtc);
    }
}

public sealed record AcceptAdminInviteCommand(string Email, string OtpCode, string NewPassword) : ICommand;

public sealed class AcceptAdminInviteCommandValidator : AbstractValidator<AcceptAdminInviteCommand>
{
    public AcceptAdminInviteCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.OtpCode).NotEmpty().Length(6);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(12);
    }
}

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
        var otp = await _otp.VerifyAsync(new OtpVerifyRequest(email, OtpPurposes.AdminInvite, request.OtpCode), cancellationToken);
        if (otp.IsFailure)
        {
            return otp;
        }

        var user = await _users.FindByEmailAsync(email, cancellationToken);
        if (user is null || user.UserType != UserType.Admin)
        {
            return Error.NotFound(MessageCodes.Auth.InvitationInvalid, "Invitation not found.");
        }

        if (user.AccountStatus != AccountStatus.Registered)
        {
            return Error.Validation(MessageCodes.Auth.InvitationInvalid, "Invitation is no longer pending.");
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

public sealed class SetAdminStatusCommandValidator : AbstractValidator<SetAdminStatusCommand>
{
    public SetAdminStatusCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

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
    private readonly IAppDbContext _db;
    private readonly ITotpAuthenticatorService _totp;

    public ListAdminUsersQueryHandler(
        IUserAccountService users,
        IAppDbContext db,
        ITotpAuthenticatorService totp)
    {
        _users = users;
        _db = db;
        _totp = totp;
    }

    public async Task<Result<IReadOnlyList<AdminUserDto>>> Handle(ListAdminUsersQuery request, CancellationToken cancellationToken)
    {
        var users = await _users.ListUsersByTypeAsync(UserType.Admin, cancellationToken);
        var result = new List<AdminUserDto>();
        foreach (var user in users)
        {
            var roles = await _users.GetRolesAsync(user.Id, cancellationToken);
            var scopes = await _db.UserScopes
                .Where(userScope => userScope.UserId == user.Id && userScope.AccessScope.IsActive)
                .OrderBy(userScope => userScope.AccessScope.ScopeType)
                .ThenBy(userScope => userScope.AccessScope.Name)
                .Select(userScope => new AdminUserScopeDto(
                    userScope.AccessScopeId,
                    userScope.AccessScope.Name,
                    userScope.AccessScope.ScopeType))
                .ToListAsync(cancellationToken);
            var twoFactorEnabled = await _totp.IsEnabledAsync(user.Id, cancellationToken);

            result.Add(new AdminUserDto(
                user.Id,
                user.Email,
                user.FullName,
                user.AccountStatus,
                user.EmailConfirmed,
                twoFactorEnabled,
                roles,
                scopes,
                user.CreatedAtUtc,
                user.LastLoginAtUtc));
        }

        return result;
    }
}

public sealed record ListAdminInvitationsQuery : IQuery<IReadOnlyList<AdminInvitationDto>>;

public sealed class ListAdminInvitationsQueryHandler
    : IQueryHandler<ListAdminInvitationsQuery, IReadOnlyList<AdminInvitationDto>>
{
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;

    public ListAdminInvitationsQueryHandler(IUserAccountService users, IAppDbContext db)
    {
        _users = users;
        _db = db;
    }

    public async Task<Result<IReadOnlyList<AdminInvitationDto>>> Handle(
        ListAdminInvitationsQuery request,
        CancellationToken cancellationToken)
    {
        var users = await _users.ListUsersByTypeAsync(UserType.Admin, cancellationToken);
        var pending = users
            .Where(user => user.AccountStatus == AccountStatus.Registered && !string.IsNullOrWhiteSpace(user.Email))
            .OrderByDescending(user => user.CreatedAtUtc);

        var result = new List<AdminInvitationDto>();
        foreach (var user in pending)
        {
            var roles = await _users.GetRolesAsync(user.Id, cancellationToken);
            var scopes = await _db.UserScopes
                .Where(userScope => userScope.UserId == user.Id && userScope.AccessScope.IsActive)
                .Select(userScope => new AdminUserScopeDto(
                    userScope.AccessScopeId,
                    userScope.AccessScope.Name,
                    userScope.AccessScope.ScopeType))
                .ToListAsync(cancellationToken);

            result.Add(new AdminInvitationDto(
                user.Id,
                user.Email!,
                user.FullName,
                roles,
                scopes,
                user.CreatedAtUtc));
        }

        return result;
    }
}

public sealed record ResendAdminInvitationCommand(Guid UserId, string Reason) : ICommand;
public sealed record RevokeAdminInvitationCommand(Guid UserId, string Reason) : ICommand;

public sealed class ResendAdminInvitationCommandValidator : AbstractValidator<ResendAdminInvitationCommand>
{
    public ResendAdminInvitationCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class RevokeAdminInvitationCommandValidator : AbstractValidator<RevokeAdminInvitationCommand>
{
    public RevokeAdminInvitationCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class ResendAdminInvitationCommandHandler : ICommandHandler<ResendAdminInvitationCommand>
{
    private readonly IUserAccountService _users;
    private readonly IOtpService _otp;
    private readonly IAuditLogger _audit;

    public ResendAdminInvitationCommandHandler(
        IUserAccountService users,
        IOtpService otp,
        IAuditLogger audit)
    {
        _users = users;
        _otp = otp;
        _audit = audit;
    }

    public async Task<Result> Handle(ResendAdminInvitationCommand request, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null ||
            user.UserType != UserType.Admin ||
            user.AccountStatus != AccountStatus.Registered ||
            string.IsNullOrWhiteSpace(user.Email))
        {
            return Error.NotFound(MessageCodes.Auth.InvitationInvalid, "Pending invitation not found.");
        }

        var sent = await _otp.SendAsync(
            new OtpSendRequest(user.Email, "Email", OtpPurposes.AdminInvite, user.Id),
            cancellationToken);
        if (sent.IsFailure)
        {
            return sent;
        }

        await _audit.LogAsync(new AuditEntry(
            "AdminInvitationResent",
            "User",
            user.Id,
            Reason: request.Reason,
            SourceChannel: "admin"), cancellationToken);

        return Result.Success();
    }
}

public sealed class RevokeAdminInvitationCommandHandler : ICommandHandler<RevokeAdminInvitationCommand>
{
    private readonly IUserAccountService _users;
    private readonly IAuditLogger _audit;

    public RevokeAdminInvitationCommandHandler(IUserAccountService users, IAuditLogger audit)
    {
        _users = users;
        _audit = audit;
    }

    public async Task<Result> Handle(RevokeAdminInvitationCommand request, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null ||
            user.UserType != UserType.Admin ||
            user.AccountStatus != AccountStatus.Registered)
        {
            return Error.NotFound(MessageCodes.Auth.InvitationInvalid, "Pending invitation not found.");
        }

        await _users.UpdateAccountStatusAsync(user.Id, AccountStatus.Closed, cancellationToken);
        await _audit.LogAsync(new AuditEntry(
            "AdminInvitationRevoked",
            "User",
            user.Id,
            Before: AccountStatus.Registered,
            After: AccountStatus.Closed,
            Reason: request.Reason,
            SourceChannel: "admin"), cancellationToken);

        return Result.Success();
    }
}

public sealed record ResetAdmin2FaCommand(Guid UserId, string Reason) : ICommand;

public sealed class ResetAdmin2FaCommandValidator : AbstractValidator<ResetAdmin2FaCommand>
{
    public ResetAdmin2FaCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty();
    }
}

public sealed class ResetAdmin2FaCommandHandler : ICommandHandler<ResetAdmin2FaCommand>
{
    private readonly IUserAccountService _users;
    private readonly ITotpAuthenticatorService _totp;
    private readonly IAuthSessionService _sessions;
    private readonly IAuthLoginChallengeService _challenges;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public ResetAdmin2FaCommandHandler(
        IUserAccountService users,
        ITotpAuthenticatorService totp,
        IAuthSessionService sessions,
        IAuthLoginChallengeService challenges,
        ICurrentUser currentUser,
        IAuditLogger audit)
    {
        _users = users;
        _totp = totp;
        _sessions = sessions;
        _challenges = challenges;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result> Handle(ResetAdmin2FaCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is null || !_currentUser.IsInRole(AppRoles.SuperAdmin))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Only SuperAdmin can reset admin 2FA.");
        }

        var user = await _users.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null || user.UserType != UserType.Admin)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "Admin user not found.");
        }

        var wasEnabled = await _totp.IsEnabledAsync(user.Id, cancellationToken);
        await _totp.ResetAsync(user.Id, cancellationToken);
        await _sessions.RevokeAllUserTokensAsync(user.Id, cancellationToken);
        await _challenges.InvalidateAllForUserAsync(user.Id, cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "Admin2FaReset",
            "User",
            user.Id,
            Before: new { TwoFactorEnabled = wasEnabled },
            After: new { TwoFactorEnabled = false },
            Reason: request.Reason,
            SourceChannel: "admin"), cancellationToken);

        return Result.Success();
    }
}
