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
    string? PhoneNumber,
    string? FullName,
    int UserType,
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
    string Reason) : ICommand<AdminInvitationDto>;

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

public sealed class InviteAdminCommandHandler : ICommandHandler<InviteAdminCommand, AdminInvitationDto>
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

    public async Task<Result<AdminInvitationDto>> Handle(InviteAdminCommand request, CancellationToken cancellationToken)
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

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        // Reject if a User already owns this email.
        var existingUser = await _users.FindByEmailAsync(normalizedEmail, cancellationToken);
        if (existingUser is not null)
        {
            return Error.Conflict(MessageCodes.Auth.DuplicateEmail, "A user with this email already exists.");
        }

        // Reject if a pending invitation already exists for this email.
        var pendingInvitation = await _db.AdminInvitations
            .FirstOrDefaultAsync(
                inv => inv.NormalizedEmail == normalizedEmail && inv.Status == AdminInvitationStatus.Pending,
                cancellationToken);
        if (pendingInvitation is not null)
        {
            return Error.Conflict(MessageCodes.Auth.DuplicateEmail, "A pending invitation already exists for this email.");
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

        var now = _clock.UtcNow;
        var invitation = new AdminInvitation
        {
            NormalizedEmail = normalizedEmail,
            FullName = request.FullName,
            RoleName = request.RoleName,
            AccessScopeId = request.AccessScopeId,
            InvitedByUserId = _currentUser.UserId.Value,
            Reason = request.Reason,
            Status = AdminInvitationStatus.Pending,
            InvitedAtUtc = now,
            CreatedAtUtc = now,
            CreatedByUserId = _currentUser.UserId,
        };

        // Send OTP correlated with the invitation ID (not a user ID).
        // The invitation ID is already generated (Guid.NewGuid in BaseEntity).
        var invitationSent = await _otp.SendAsync(
            new OtpSendRequest(
                normalizedEmail,
                "Email",
                OtpPurposes.AdminInvite,
                invitation.Id,
                TemplateVariables: new Dictionary<string, string>
                {
                    ["recipient_name"] = request.FullName,
                }),
            cancellationToken);
        if (invitationSent.IsFailure)
        {
            return invitationSent.Error;
        }

        // Stage the invitation only after successful delivery. OtpService persists its
        // challenge independently, so a delivery failure cannot leave a pending invite.
        _db.AdminInvitations.Add(invitation);

        await _audit.LogAsync(new AuditEntry(
            "AdminUserInvited",
            "AdminInvitation",
            invitation.Id,
            After: new { request.Email, request.RoleName, request.AccessScopeId },
            Reason: request.Reason,
            SourceChannel: "admin"), cancellationToken);

        // Wire compatibility: UserId contains the invitation ID until acceptance.
        return new AdminInvitationDto(
            invitation.Id,
            normalizedEmail,
            request.FullName,
            [request.RoleName],
            requestedScope is null
                ? Array.Empty<AdminUserScopeDto>()
                : [new AdminUserScopeDto(requestedScope.Id, requestedScope.Name, requestedScope.ScopeType)],
            invitation.InvitedAtUtc);
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
    private readonly IAppDbContext _db;
    private readonly IOtpService _otp;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public AcceptAdminInviteCommandHandler(
        IUserAccountService users,
        IAppDbContext db,
        IOtpService otp,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _users = users;
        _db = db;
        _otp = otp;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result> Handle(AcceptAdminInviteCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        // Verify OTP first. OTP failure must never create a User.
        var otp = await _otp.VerifyAsync(
            new OtpVerifyRequest(email, OtpPurposes.AdminInvite, request.OtpCode),
            cancellationToken);
        if (otp.IsFailure)
        {
            return otp;
        }

        // Load pending invitation by normalized email.
        var invitation = await _db.AdminInvitations
            .FirstOrDefaultAsync(
                inv => inv.NormalizedEmail == email && inv.Status == AdminInvitationStatus.Pending,
                cancellationToken);
        if (invitation is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvitationInvalid, "Invitation not found.");
        }

        // Reject if a User already owns this email (race condition guard).
        var existingUser = await _users.FindByEmailAsync(email, cancellationToken);
        if (existingUser is not null)
        {
            return Error.Conflict(MessageCodes.Auth.DuplicateEmail, "A user with this email already exists.");
        }

        // Create the real user with the submitted password.
        var create = await _users.CreateAsync(new CreateUserRequest(
            email,
            null,
            request.NewPassword,
            invitation.FullName,
            UserType.Admin,
            AccountStatus.Active), cancellationToken);

        if (create.IsFailure)
        {
            return create.Error;
        }

        // Assign the stored role.
        var roleAssignment = await _users.AddToRoleAsync(create.Value.Id, invitation.RoleName, cancellationToken);
        if (roleAssignment.IsFailure)
        {
            return roleAssignment.Error;
        }

        // Stage the optional scope.
        if (invitation.AccessScopeId is { } scopeId)
        {
            _db.UserScopes.Add(new UserScope
            {
                UserId = create.Value.Id,
                AccessScopeId = scopeId,
                CreatedAtUtc = _clock.UtcNow,
            });
        }

        // Confirm email.
        await _users.SetEmailConfirmedAsync(create.Value.Id, true, cancellationToken);

        // Mark invitation as accepted.
        var now = _clock.UtcNow;
        invitation.Status = AdminInvitationStatus.Accepted;
        invitation.AcceptedAtUtc = now;
        invitation.AcceptedUserId = create.Value.Id;
        invitation.ModifiedAtUtc = now;

        await _audit.LogAsync(new AuditEntry(
            "AdminInviteAccepted",
            "AdminInvitation",
            invitation.Id,
            After: new { UserId = create.Value.Id, invitation.RoleName },
            SourceChannel: "admin"), cancellationToken);

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
        var adminUsers = await _users.ListUsersByTypeAsync(UserType.Admin, cancellationToken);
        var customerUsers = await _users.ListUsersByTypeAsync(UserType.Customer, cancellationToken);
        var allUsers = adminUsers.Concat(customerUsers).ToList();

        var result = new List<AdminUserDto>();
        foreach (var user in allUsers)
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
            var twoFactorEnabled = user.UserType == UserType.Admin
                ? await _totp.IsEnabledAsync(user.Id, cancellationToken)
                : false;

            result.Add(new AdminUserDto(
                user.Id,
                user.Email,
                user.PhoneNumber,
                user.FullName,
                (int)user.UserType,
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
    private readonly IAppDbContext _db;

    public ListAdminInvitationsQueryHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<AdminInvitationDto>>> Handle(
        ListAdminInvitationsQuery request,
        CancellationToken cancellationToken)
    {
        var pending = await _db.AdminInvitations
            .Where(inv => inv.Status == AdminInvitationStatus.Pending)
            .OrderByDescending(inv => inv.InvitedAtUtc)
            .Select(inv => new
            {
                inv.Id,
                inv.NormalizedEmail,
                inv.FullName,
                inv.RoleName,
                inv.AccessScopeId,
                inv.InvitedAtUtc,
                ScopeName = inv.AccessScope != null ? inv.AccessScope.Name : null,
                ScopeType = inv.AccessScope != null ? (AccessScopeType?)inv.AccessScope.ScopeType : null,
            })
            .ToListAsync(cancellationToken);

        var result = new List<AdminInvitationDto>();
        foreach (var inv in pending)
        {
            var scopes = inv.AccessScopeId.HasValue && inv.ScopeName is not null && inv.ScopeType.HasValue
                ? new[] { new AdminUserScopeDto(inv.AccessScopeId.Value, inv.ScopeName, inv.ScopeType.Value) }
                : Array.Empty<AdminUserScopeDto>();

            // Wire compatibility: UserId property holds invitation.Id.
            result.Add(new AdminInvitationDto(
                inv.Id,
                inv.NormalizedEmail,
                inv.FullName,
                [inv.RoleName],
                scopes,
                inv.InvitedAtUtc));
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
    private readonly IAppDbContext _db;
    private readonly IOtpService _otp;
    private readonly IAuditLogger _audit;

    public ResendAdminInvitationCommandHandler(
        IAppDbContext db,
        IOtpService otp,
        IAuditLogger audit)
    {
        _db = db;
        _otp = otp;
        _audit = audit;
    }

    public async Task<Result> Handle(ResendAdminInvitationCommand request, CancellationToken cancellationToken)
    {
        // Load pending invitation by ID (the "UserId" in the URL is actually the invitation ID).
        var invitation = await _db.AdminInvitations
            .FirstOrDefaultAsync(
                inv => inv.Id == request.UserId && inv.Status == AdminInvitationStatus.Pending,
                cancellationToken);
        if (invitation is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvitationInvalid, "Pending invitation not found.");
        }

        var sent = await _otp.SendAsync(
            new OtpSendRequest(
                invitation.NormalizedEmail,
                "Email",
                OtpPurposes.AdminInvite,
                invitation.Id,
                TemplateVariables: new Dictionary<string, string>
                {
                    ["recipient_name"] = invitation.FullName,
                }),
            cancellationToken);
        if (sent.IsFailure)
        {
            return sent;
        }

        await _audit.LogAsync(new AuditEntry(
            "AdminInvitationResent",
            "AdminInvitation",
            invitation.Id,
            Reason: request.Reason,
            SourceChannel: "admin"), cancellationToken);

        return Result.Success();
    }
}

public sealed class RevokeAdminInvitationCommandHandler : ICommandHandler<RevokeAdminInvitationCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public RevokeAdminInvitationCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result> Handle(RevokeAdminInvitationCommand request, CancellationToken cancellationToken)
    {
        // Load pending invitation by ID (the "UserId" in the URL is actually the invitation ID).
        var invitation = await _db.AdminInvitations
            .FirstOrDefaultAsync(
                inv => inv.Id == request.UserId && inv.Status == AdminInvitationStatus.Pending,
                cancellationToken);
        if (invitation is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvitationInvalid, "Pending invitation not found.");
        }

        var now = _clock.UtcNow;
        invitation.Status = AdminInvitationStatus.Revoked;
        invitation.RevokedAtUtc = now;
        invitation.RevokedReason = request.Reason;
        invitation.RevokedByUserId = _currentUser.UserId;
        invitation.ModifiedAtUtc = now;

        await _audit.LogAsync(new AuditEntry(
            "AdminInvitationRevoked",
            "AdminInvitation",
            invitation.Id,
            Before: AdminInvitationStatus.Pending,
            After: AdminInvitationStatus.Revoked,
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
